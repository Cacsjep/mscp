#Requires -Version 5.1
param(
    [string]$Server = "localhost",
    [string]$User   = "test12",
    [string]$Pass   = '3284n)(Bdb6sd'
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAll : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAll
[System.Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$base = "https://$Server"

# Token
$tokenBody = "grant_type=password&username=$([uri]::EscapeDataString($User))&password=$([uri]::EscapeDataString($Pass))&client_id=GrantValidatorClient"
$token = $null
try {
    $r = Invoke-RestMethod -Method Post -Uri "$base/IDP/connect/token" -Body $tokenBody `
         -ContentType 'application/x-www-form-urlencoded' -TimeoutSec 8
    $token = $r.access_token
    Write-Host "Token OK (expires in $($r.expires_in)s)" -ForegroundColor Green
} catch {
    Write-Host "Token FAILED $_" -ForegroundColor Red
    exit 1
}

# Use HttpClient via .NET so each request is independent
Add-Type -AssemblyName System.Net.Http

function Probe([string]$path) {
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(8)
    $client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
    $client.DefaultRequestHeaders.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new('application/json'))
    try {
        $resp = $client.GetAsync("$base$path").GetAwaiter().GetResult()
        $status = [int]$resp.StatusCode
        $bodyBytes = $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $body = if ($bodyBytes) { [System.Text.Encoding]::UTF8.GetString($bodyBytes) } else { '' }
        $color = if ($status -lt 300) { 'Green' } elseif ($status -lt 500) { 'Yellow' } else { 'Red' }
        Write-Host "$status  GET $path" -ForegroundColor $color
        if ($body) {
            $preview = if ($body.Length -gt 800) { $body.Substring(0, 800) + " ...(truncated, total=$($body.Length))" } else { $body }
            Write-Host "  $preview" -ForegroundColor DarkGray
        }
    } catch {
        Write-Host "EXC GET $path  $_" -ForegroundColor Red
    } finally {
        $client.Dispose()
    }
    Write-Host ""
}

# Discovery - find the gateway and what it exposes
Probe '/api/rest/v1/'
Probe '/api/rest/v1/swagger'
Probe '/api/rest/v1/swagger/v1/swagger.json'
Probe '/api/rest/v1/openapi.json'
Probe '/api/rest/v1/openapi'
Probe '/api/rest/v1/$metadata'

# Built-in items - sanity check that REST works at all for this user
Probe '/api/rest/v1/cameras?top=1'
Probe '/api/rest/v1/recordingServers?top=1'
Probe '/api/rest/v1/users?top=1'

# MIP custom items - the actual question
Probe '/api/rest/v1/mipKinds'
Probe '/api/rest/v1/mipItems'
Probe '/api/rest/v1/mipKinds?top=10'
Probe '/api/rest/v1/mipKinds/A6027637-6C03-4C58-A6DB-7B837C74AA60'
Probe '/api/rest/v1/mipKinds/A6027637-6C03-4C58-A6DB-7B837C74AA60/mipItems'
Probe '/api/rest/v1/mipKinds/25BAE827-E09F-48C5-BB14-2072EA0573C2/mipItems'
