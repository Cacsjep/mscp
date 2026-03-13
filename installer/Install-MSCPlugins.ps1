param(
    [string]$MsiPath
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $MsiPath) {
    $msi = Get-ChildItem -Path $scriptDir -Filter 'MSCPlugins-*.msi' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $msi) {
        throw "No MSCPlugins MSI found in $scriptDir."
    }
    $MsiPath = $msi.FullName
}

$msiFile = Get-Item $MsiPath
$logPath = Join-Path $msiFile.DirectoryName ([System.IO.Path]::GetFileNameWithoutExtension($msiFile.Name) + '.log')

Write-Host "MSI: $($msiFile.FullName)"
Write-Host "Log: $logPath"

$arguments = @(
    '/i'
    "`"$($msiFile.FullName)`""
    '/L*v'
    "`"$logPath`""
)

$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru
exit $process.ExitCode
