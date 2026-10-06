<#
.SYNOPSIS
    Baixa e instala/atualiza o RPA Version Control a partir da release mais
    recente publicada no GitHub.

.DESCRIPTION
    Pensado para distribuição corporativa via linha de comando, sem precisar
    abrir o navegador ou copiar arquivos manualmente. Sempre baixa o build
    mais recente de main (tag "latest" da release), então uma mesma URL
    serve para instalar e para atualizar.

.PARAMETER InstallDir
    Pasta onde o app será extraído. Padrão: %LOCALAPPDATA%\RpaVersionControl.

.PARAMETER SharedRoot
    Caminho de rede da pasta compartilhada do RPA Version Control (ex.:
    \\servidor\pasta\RPA-Version-Control). Se informado, grava o
    shared-root.txt automaticamente para que o usuário não precise
    configurar isso na primeira execução.

.PARAMETER Shortcut
    Cria um atalho na área de trabalho do usuário atual.

.PARAMETER Launch
    Abre o app ao final da instalação/atualização.

.EXAMPLE
    Instalação rápida com as opções padrão (uso em um único comando):

    irm https://raw.githubusercontent.com/ValberRodr/rpa-version-control/main/scripts/install.ps1 | iex

.EXAMPLE
    Instalação com pasta de rede e atalho já configurados — baixe o script
    primeiro para poder passar parâmetros:

    iwr https://raw.githubusercontent.com/ValberRodr/rpa-version-control/main/scripts/install.ps1 -OutFile install.ps1
    .\install.ps1 -SharedRoot "\\servidor\pasta\RPA-Version-Control" -Shortcut -Launch
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "RpaVersionControl"),
    [string]$SharedRoot,
    [switch]$Shortcut,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$releaseUrl = "https://github.com/ValberRodr/rpa-version-control/releases/latest/download/RpaVersionControl-win-x64.zip"
$exeName = "RpaVersionControl.App.exe"

Write-Host "== RPA Version Control — instalação/atualização ==" -ForegroundColor Cyan
Write-Host "Baixando build mais recente de $releaseUrl"

$zipPath = Join-Path $env:TEMP "RpaVersionControl-$([Guid]::NewGuid().ToString('N')).zip"
try {
    Invoke-WebRequest -Uri $releaseUrl -OutFile $zipPath -UseBasicParsing
}
catch {
    Write-Error "Falha ao baixar o build. Verifique sua conexão com a internet/GitHub. Detalhe: $($_.Exception.Message)"
    exit 1
}

if (Test-Path $InstallDir) {
    Write-Host "Atualizando instalação existente em $InstallDir"
    Get-ChildItem -Path $InstallDir -Recurse -File |
        Where-Object { $_.Name -ne "shared-root.txt" } |
        Remove-Item -Force
}
else {
    Write-Host "Instalando em $InstallDir"
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
}

Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
Remove-Item $zipPath -Force

if ($SharedRoot) {
    Set-Content -Path (Join-Path $InstallDir "shared-root.txt") -Value $SharedRoot -Encoding UTF8
    Write-Host "Pasta compartilhada configurada: $SharedRoot"
}

$exePath = Join-Path $InstallDir $exeName
if (-not (Test-Path $exePath)) {
    Write-Error "Instalação concluída, mas $exeName não foi encontrado em $InstallDir. O conteúdo do zip pode ter mudado."
    exit 1
}

if ($Shortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut((Join-Path $desktop "RPA Version Control.lnk"))
    $link.TargetPath = $exePath
    $link.WorkingDirectory = $InstallDir
    $link.Save()
    Write-Host "Atalho criado na área de trabalho."
}

Write-Host "Instalação concluída: $exePath" -ForegroundColor Green

if ($Launch) {
    Start-Process -FilePath $exePath
}
