using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mscp.PkiCertInstaller.Models;

namespace Mscp.PkiCertInstaller.Services;

// Thrown from the TLS validator when the server's certificate is not
// trusted by the OS chain AND not present in our per-user TrustStore.
// Carries the cert details so the UI can show a meaningful prompt.
public sealed class UntrustedServerCertException : Exception
{
    public string Host { get; }
    public int    Port { get; }
    public string Thumbprint { get; }
    public string Subject { get; }
    public string Issuer { get; }
    public DateTime NotBefore { get; }
    public DateTime NotAfter  { get; }
    public string Reason { get; }

    public UntrustedServerCertException(
        string host, int port, string thumbprint, string subject, string issuer,
        DateTime notBefore, DateTime notAfter, string reason)
        : base($"Server certificate not trusted ({reason})")
    {
        Host = host; Port = port; Thumbprint = thumbprint;
        Subject = subject; Issuer = issuer;
        NotBefore = notBefore; NotAfter = notAfter; Reason = reason;
    }
}

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
    private readonly TrustStore _trustStore;
    private string? _token;

    public MilestoneClient(string serverUrl, TrustStore trustStore)
    {
        _baseUri = NormalizeBase(serverUrl);
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _http = BuildHttpClient(useDefaultCreds: false);
    }

    public Uri BaseUri => _baseUri;

    private HttpClient BuildHttpClient(bool useDefaultCreds)
    {
        var handler = new HttpClientHandler
        {
            // Strict TLS validation by default. On chain failure we
            // consult the per-user TrustStore for a thumbprint pin
            // matching this host:port; if that's a hit the connection
            // proceeds, otherwise we throw a typed exception that the
            // UI catches to prompt the admin.
            ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;

                var host = request?.RequestUri?.Host ?? _baseUri.Host;
                var port = request?.RequestUri?.Port > 0
                    ? request!.RequestUri!.Port : _baseUri.Port;
                var thumb = cert?.Thumbprint ?? "";

                if (!string.IsNullOrEmpty(thumb) && _trustStore.IsTrusted(host, port, thumb))
                    return true;

                var reason = errors switch
                {
                    SslPolicyErrors.RemoteCertificateNotAvailable => "no certificate presented",
                    SslPolicyErrors.RemoteCertificateNameMismatch => "hostname does not match certificate",
                    SslPolicyErrors.RemoteCertificateChainErrors  => "certificate chain not trusted",
                    _ => errors.ToString(),
                };
                throw new UntrustedServerCertException(
                    host, port, thumb,
                    cert?.Subject ?? "",
                    cert?.Issuer  ?? "",
                    cert?.NotBefore ?? DateTime.MinValue,
                    cert?.NotAfter  ?? DateTime.MinValue,
                    reason);
            },
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

    // Walks an exception chain to find an UntrustedServerCertException.
    // The validator throws this; HttpClient wraps it (typically as the
    // inner of an HttpRequestException). The UI uses this to decide
    // whether to show the trust prompt or the generic error path.
    public static UntrustedServerCertException? FindTrustException(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e is UntrustedServerCertException u) return u;
        return null;
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
            MipItemsResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<MipItemsResponse>(text, _jsonOpts);
            }
            catch (JsonException jex)
            {
                throw new InvalidOperationException(
                    $"Couldn't parse {kind.Label} mipItems response: {jex.Message}", jex);
            }
            if (response?.Array == null) continue;
            foreach (var dto in response.Array)
            {
                if (dto == null) continue;
                if (string.IsNullOrEmpty(dto.Thumbprint))
                {
                    Log.Warn($"Skipping {kind.Label} item '{dto.Name}' - no Thumbprint field on the server response.");
                    continue;
                }
                result.Add(MapItem(dto, kind));
            }
        }
        return result;
    }

    // Strict JSON shape for the mipItems REST response. Marking known
    // fields with [JsonRequired] would crash on every legacy server
    // that omits a field, so we instead VALIDATE in code (Thumbprint
    // is mandatory, others are best-effort) and log when something
    // unexpected is missing. That keeps the installer working against
    // older servers without silently dropping rows.
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class MipItemsResponse
    {
        [JsonPropertyName("array")]
        public List<MipItemDto>? Array { get; set; }
    }

    private sealed class MipItemDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Subject { get; set; }
        public string? Issuer { get; set; }
        public string? Thumbprint { get; set; }
        public string? IssuerThumbprint { get; set; }
        public string? SerialNumber { get; set; }
        public string? NotBefore { get; set; }
        public string? NotAfter { get; set; }
        public string? KeyAlgorithm { get; set; }
        public string? HasPrivateKey { get; set; }
        public string? Pfx { get; set; }
        public string? Der { get; set; }
        public string? SubjectAlternativeNames { get; set; }

        // Legacy server keys (pre-rename). Read-only mapping so older
        // Mgmt Servers keep working until they're upgraded.
        public string? EncryptedPfx { get; set; }
        public string? EncryptedDer { get; set; }
    }

    private static CertItem MapItem(MipItemDto dto, CertKind kind)
    {
        DateTime? ParseDate(string? s)
            => DateTime.TryParse(s, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;

        var pfx = !string.IsNullOrEmpty(dto.Pfx) ? dto.Pfx : (dto.EncryptedPfx ?? "");
        var der = !string.IsNullOrEmpty(dto.Der) ? dto.Der : (dto.EncryptedDer ?? "");

        return new CertItem
        {
            Id = Guid.TryParse(dto.Id, out var g) ? g : Guid.Empty,
            Kind = kind.Id,
            KindLabel = kind.Label,
            Name = dto.Name ?? "",
            Subject = dto.Subject ?? "",
            Issuer = dto.Issuer ?? "",
            Thumbprint = dto.Thumbprint ?? "",
            IssuerThumbprint = dto.IssuerThumbprint ?? "",
            SerialNumber = dto.SerialNumber ?? "",
            NotBefore = ParseDate(dto.NotBefore),
            NotAfter  = ParseDate(dto.NotAfter),
            KeyAlgorithm = dto.KeyAlgorithm ?? "",
            HasPrivateKey = string.Equals(dto.HasPrivateKey, "True", StringComparison.OrdinalIgnoreCase),
            PfxBase64 = pfx,
            DerBase64 = der,
            SubjectAlternativeNames = dto.SubjectAlternativeNames ?? "",
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
