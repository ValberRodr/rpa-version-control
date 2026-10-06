param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$out = Join-Path $artifacts $Runtime

Write-Host "== Restore =="
dotnet restore (Join-Path $root "RpaVersionControl.sln")

Write-Host "== Tests =="
dotnet test (Join-Path $root "tests\RpaVersionControl.Core.Tests\RpaVersionControl.Core.Tests.csproj") `
    -c $Configuration --no-restore

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "== Publish Windows x64 =="
dotnet publish (Join-Path $root "src\RpaVersionControl.App\RpaVersionControl.App.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -o $out

$sidecar = Join-Path $out "shared-root.txt"
if (-not (Test-Path $sidecar)) {
    Set-Content -Path $sidecar -Value "\\servidor\pasta\RPA-Version-Control" -Encoding UTF8
}

$zip = Join-Path $artifacts "RpaVersionControl-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host ""
Write-Host "Build concluído:"
Write-Host "  App: $out"
Write-Host "  ZIP: $zip"
Write-Host ""
Write-Host "Ajuste shared-root.txt antes de distribuir."
