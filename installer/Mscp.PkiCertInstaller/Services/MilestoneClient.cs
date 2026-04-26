using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mscp.PkiCertInstaller.Models;

namespace Mscp.PkiCertInstaller.Services;

// Talks to the Milestone Mgmt Server's REST + IDP token endpoints.
// Two surfaces:
//   1. Login() exchanges username/password for an OAuth bearer token via
//      POST /IDP/connect/token (grant_type=password for Basic users,
//      grant_type=windows_credentials for Windows users).
//   2. ListPkiCerts() pulls every cert under the PKI plugin's five kinds
//      via GET /api/rest/v1/mipKinds/{kindId}/mipItems and projects them
//      into CertItem records.
public sealed class MilestoneClient : IDisposable
{
    // Hard-coded - matches the PKI plugin's PKIDefinition.cs.
    public static readonly Guid PluginId = new("A6027637-6C03-4C58-A6DB-7B837C74AA60");

    public sealed record CertKind(Guid Id, string Label);

    public static readonly IReadOnlyList<CertKind> CertKinds = new[]
    {
        new CertKind(new Guid("25BAE827-E09F-48C5-BB14-2072EA0573C2"), "Root CA"),
        new CertKind(new Guid("1614CDD4-DBE4-44E7-AAF5-17D347087006"), "Intermediate CA"),
        new CertKind(new Guid("53EEFF60-02F5-40B4-9C9C-65DA698CBEDA"), "HTTPS"),
        new CertKind(new Guid("AE29EE2B-8D42-4A13-BE55-15DEA4C4D029"), "802.1X"),
        new CertKind(new Guid("3E264439-F043-4333-901C-951D5B5E5ACC"), "Service"),
    };

    private const string ClientId = "GrantValidatorClient";

    private HttpClient _http;
    private readonly Uri _baseUri;
    private string? _token;

    public MilestoneClient(string serverUrl)
    {
        _baseUri = NormalizeBase(serverUrl);
        _http = BuildHttpClient(useDefaultCreds: false);
    }

    public Uri BaseUri => _baseUri;

    private static HttpClient BuildHttpClient(bool useDefaultCreds)
    {
        // Self-signed certs are normal on Milestone Mgmt Servers, so we
        // accept anything during the bearer-token exchange. The token
        // itself is signed by the Milestone IDP (RS256) so transport-
        // level verification is not what's gating trust here.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AutomaticDecompression = DecompressionMethods.All,
            UseDefaultCredentials  = useDefaultCreds,
        };
        // No artificial timeout - some Mgmt Servers do a slow IDP cold
        // start (~20-30s on first hit after a service restart) and
        // we'd rather let the user wait than fail with a confusing
        // "request was canceled" error. The user can always cancel
        // by closing the window.
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    public async Task LoginBasicAsync(string username, string password, CancellationToken ct = default)
    {
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("grant_type",   "password"),
            new KeyValuePair<string,string>("username",     username),
            new KeyValuePair<string,string>("password",     password),
            new KeyValuePair<string,string>("client_id",    ClientId),
        });
        await ExchangeTokenAsync(_http, body, ct);
    }

    // Explicit Windows creds: send username + password to the
    // windows_credentials grant. The IDP uses LogonUser internally.
    public async Task LoginWindowsExplicitAsync(string username, string password, CancellationToken ct = default)
    {
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("grant_type", "windows_credentials"),
            new KeyValuePair<string,string>("username",   username),
            new KeyValuePair<string,string>("password",   password),
            new KeyValuePair<string,string>("client_id",  ClientId),
        });
        await ExchangeTokenAsync(_http, body, ct);
    }

    // Current Windows session: no creds in the body. The token endpoint
    // requires a Negotiate (NTLM/Kerberos) handshake on the HTTP layer,
    // so we spin up a separate HttpClient with UseDefaultCredentials
    // for the exchange, then keep using the original (no-creds) client
    // with the bearer token for subsequent REST calls.
    public async Task LoginWindowsCurrentUserAsync(CancellationToken ct = default)
    {
        using var winClient = BuildHttpClient(useDefaultCreds: true);
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("grant_type", "windows_credentials"),
            new KeyValuePair<string,string>("client_id",  ClientId),
        });
        await ExchangeTokenAsync(winClient, body, ct);
    }

    private async Task ExchangeTokenAsync(HttpClient client, HttpContent body, CancellationToken ct)
    {
        var url = new Uri(_baseUri, "IDP/connect/token");
        using var resp = await client.PostAsync(url, body, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"IDP {(int)resp.StatusCode}: {Trim(text)}");
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("access_token", out var tok))
            throw new InvalidOperationException("IDP response missing access_token");
        _token = tok.GetString();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    public async Task<IReadOnlyList<CertItem>> ListPkiCertsAsync(CancellationToken ct = default)
    {
        var result = new List<CertItem>();
        foreach (var kind in CertKinds)
        {
            var url = new Uri(_baseUri, $"api/rest/v1/mipKinds/{kind.Id:D}/mipItems");
            using var resp = await _http.GetAsync(url, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"GET {kind.Label} mipItems {(int)resp.StatusCode}: {Trim(text)}");
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("array", out var arr) ||
                arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var el in arr.EnumerateArray())
                result.Add(MapItem(el, kind));
        }
        return result;
    }

    // Project a single REST item shape onto our CertItem record. The
    // properties bag comes back flattened into the same JSON object as
    // the framework keys (Id, Name, FQID, etc.), so we just probe each
    // key by name.
    private static CertItem MapItem(JsonElement el, CertKind kind)
    {
        string Get(string k) =>
            el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
        DateTime? GetDate(string k)
            => DateTime.TryParse(Get(k), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;

        // Backwards-compat: read both the new "Pfx"/"Der" keys and the
        // legacy "EncryptedPfx"/"EncryptedDer" keys. Encrypted blobs
        // can't be opened by us (DPAPI machine scope on the mgmt
        // server), but we still surface the row so the admin sees it.
        var pfx = Get("Pfx");
        if (pfx.Length == 0) pfx = Get("EncryptedPfx");
        var der = Get("Der");
        if (der.Length == 0) der = Get("EncryptedDer");

        return new CertItem
        {
            Id = el.TryGetProperty("Id", out var idEl) && Guid.TryParse(idEl.GetString(), out var g)
                ? g : Guid.Empty,
            Kind = kind.Id,
            KindLabel = kind.Label,
            Name = Get("Name"),
            Subject = Get("Subject"),
            Issuer = Get("Issuer"),
            Thumbprint = Get("Thumbprint"),
            IssuerThumbprint = Get("IssuerThumbprint"),
            SerialNumber = Get("SerialNumber"),
            NotBefore = GetDate("NotBefore"),
            NotAfter  = GetDate("NotAfter"),
            KeyAlgorithm = Get("KeyAlgorithm"),
            HasPrivateKey = string.Equals(Get("HasPrivateKey"), "True", StringComparison.OrdinalIgnoreCase),
            PfxBase64 = pfx,
            DerBase64 = der,
            SubjectAlternativeNames = Get("SubjectAlternativeNames"),
        };
    }

    private static Uri NormalizeBase(string serverUrl)
    {
        var s = (serverUrl ?? "").Trim();
        if (s.Length == 0) throw new ArgumentException("Server URL is required");
        if (!s.Contains("://", StringComparison.Ordinal)) s = "https://" + s;
        if (!s.EndsWith('/')) s += "/";
        return new Uri(s);
    }

    private static string Trim(string s)
        => s.Length > 240 ? s[..240] + "..." : s;

    public void Dispose() => _http.Dispose();
}
