<#
.SYNOPSIS
    Baixa o build mais recente do RPA Version Control e extrai para uma pasta
    local. Não instala nada: não precisa de permissão de administrador, não
    grava no registro, não altera o sistema. É só download + descompactação.

.PARAMETER Destination
    Pasta onde o app será extraído. Padrão: %USERPROFILE%\Downloads\RpaVersionControl.

.PARAMETER Proxy
    Endereço do proxy corporativo (ex.: http://proxy.empresa.local:8080).
    Também pode incluir usuário/senha na própria URL:
    http://usuario:senha@proxy.empresa.local:8080.

.PARAMETER ProxyUseDefaultCredentials
    Usa as credenciais do usuário Windows atual para autenticar no proxy —
    útil quando o proxy usa NTLM/Kerberos em vez de usuário/senha na URL.

.EXAMPLE
    .\download-exe.ps1 -Proxy "http://proxy.empresa.local:8080"

.EXAMPLE
    .\download-exe.ps1 -Proxy "http://proxy.empresa.local:8080" -ProxyUseDefaultCredentials

.EXAMPLE
    Sem proxy (rede sem proxy explícito):

    .\download-exe.ps1
#>
param(
    [string]$Destination = (Join-Path $env:USERPROFILE "Downloads\RpaVersionControl"),
    [string]$Proxy,
    [switch]$ProxyUseDefaultCredentials
)

$ErrorActionPreference = "Stop"
$releaseUrl = "https://github.com/ValberRodr/rpa-version-control/releases/latest/download/RpaVersionControl-win-x64.zip"
$zipPath = Join-Path $env:TEMP "RpaVersionControl-$([Guid]::NewGuid().ToString('N')).zip"

$webArgs = @{ Uri = $releaseUrl; OutFile = $zipPath; UseBasicParsing = $true }
if ($Proxy) {
    $webArgs.Proxy = $Proxy
    if ($ProxyUseDefaultCredentials) { $webArgs.ProxyUseDefaultCredentials = $true }
}

Write-Host "Baixando build mais recente..."
try {
    Invoke-WebRequest @webArgs
}
catch {
    Write-Error "Falha ao baixar. Se a rede exigir proxy, use -Proxy 'http://proxy:porta' (e -ProxyUseDefaultCredentials se necessário). Detalhe: $($_.Exception.Message)"
    exit 1
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $Destination -Force
Remove-Item $zipPath -Force

Write-Host ""
Write-Host "Pronto: $Destination\RpaVersionControl.App.exe" -ForegroundColor Green
Write-Host "Nada foi instalado no sistema - e um app portatil; apague a pasta para remover."
