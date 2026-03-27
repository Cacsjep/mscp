$base = "http://localhost:9500"
$token = "81f9765e14fc2b7a83088d98b94ac899"
$headers = @{ Authorization = "Bearer $token" }

function Test($method, $path, $body = $null) {
    $url = "$base$path"
    Write-Host ""
    Write-Host "=== $method $path ===" -ForegroundColor Cyan
    try {
        $params = @{
            Uri = $url
            Method = $method
            Headers = $headers
            ContentType = "application/json"
        }
        if ($body) {
            $params.Body = ($body | ConvertTo-Json -Compress)
        }
        $r = Invoke-RestMethod @params
        $r | ConvertTo-Json -Depth 5
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        $detail = $_.ErrorDetails.Message
        Write-Host "HTTP $code" -ForegroundColor Red
        if ($detail) { Write-Host $detail -ForegroundColor Red }
    }
}

Write-Host "========================================" -ForegroundColor Yellow
Write-Host " SC Remote Control API Test" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow

# --- Auth test ---
Write-Host ""
Write-Host "=== Auth: no token ===" -ForegroundColor Cyan
try {
    Invoke-RestMethod -Uri "$base/api/status" -Method GET -ContentType "application/json"
} catch {
    Write-Host "HTTP $($_.Exception.Response.StatusCode.value__) (expected 401)" -ForegroundColor Green
}

# --- Discovery endpoints ---
Test "GET" "/api/status"
Test "GET" "/api/views"
Test "GET" "/api/cameras"
Test "GET" "/api/workspaces"
Test "GET" "/api/windows"

# --- Grab IDs for action tests ---
Write-Host ""
Write-Host "=== Collecting IDs for action tests ===" -ForegroundColor Yellow

$views = Invoke-RestMethod -Uri "$base/api/views" -Method GET -Headers $headers -ContentType "application/json"
$cameras = Invoke-RestMethod -Uri "$base/api/cameras" -Method GET -Headers $headers -ContentType "application/json"
$workspaces = Invoke-RestMethod -Uri "$base/api/workspaces" -Method GET -Headers $headers -ContentType "application/json"

$viewId = if ($views.Count -gt 0) { $views[0].id } else { $null }
$camIds = @()
if ($cameras -is [array]) {
    if ($cameras.Count -ge 4) { $camIds = @($cameras[0..3] | ForEach-Object { $_.id }) }
    else { $camIds = @($cameras | ForEach-Object { $_.id }) }
} elseif ($cameras) {
    $camIds = @($cameras.id)
}
$wsId = if ($workspaces.Count -gt 0) { $workspaces[0].id } else { $null }

Write-Host "  First view:  $viewId"
Write-Host "  Cameras:     $($camIds.Count) selected"
Write-Host "  First WS:    $wsId"

# --- Action endpoints ---
if ($viewId) {
    Test "POST" "/api/views/switch" @{ viewId = $viewId; windowIndex = 0 }
} else {
    Write-Host "SKIP /api/views/switch (no views)" -ForegroundColor DarkGray
}

if ($camIds.Count -gt 0) {
    Test "POST" "/api/cameras/show" @{ cameraIds = $camIds; windowIndex = 0 }
    Start-Sleep -Seconds 2
    Test "POST" "/api/cameras/set" @{ cameraId = $camIds[0]; slotIndex = 0; windowIndex = 0 }
} else {
    Write-Host "SKIP /api/cameras/show + set (no cameras)" -ForegroundColor DarkGray
}

if ($wsId) {
    Test "POST" "/api/workspaces/switch" @{ workspaceId = $wsId; windowIndex = 0 }
}

Test "POST" "/api/mode/change" @{ mode = "Normal" }

Test "POST" "/api/application/control" @{ command = "ToggleFullscreen" }
Start-Sleep -Seconds 2
Test "POST" "/api/application/control" @{ command = "ToggleFullscreen" }

# --- Clear with delay ---
Test "POST" "/api/clear" @{ windowIndex = 0; delaySeconds = 3 }

# --- Validation tests ---
Write-Host ""
Write-Host "=== Validation / Error tests ===" -ForegroundColor Yellow
Test "POST" "/api/views/switch" @{ windowIndex = 0 }
Test "POST" "/api/cameras/show" @{ cameraIds = @() }
Test "POST" "/api/mode/change" @{ mode = "InvalidMode" }
Test "POST" "/api/application/control" @{ command = "DoesNotExist" }
Test "POST" "/api/cameras/set" @{ cameraId = "00000000-0000-0000-0000-000000000000" }

Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host " Done!" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
