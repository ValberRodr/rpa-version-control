<#
TEMPLATE — revise com a equipe de infraestrutura antes de executar.
O aplicativo usa as credenciais Windows atuais. As ACLs da rede são o enforcement real.
#>
param(
    [Parameter(Mandatory=$true)][string]$Root,
    [Parameter(Mandatory=$true)][string]$DevGroup,
    [Parameter(Mandatory=$true)][string]$QaGroup,
    [Parameter(Mandatory=$true)][string]$AdminGroup
)

$ErrorActionPreference = "Stop"

$paths = @(
    "config", "projects", "changes", "repositories", "locks", "audit"
)
foreach ($p in $paths) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Root $p) | Out-Null
}

Write-Warning "Este script é apenas um ponto de partida. Valide herança, CREATOR OWNER e política corporativa."
Write-Host "Recomendação:"
Write-Host " - config/projects: Admin=Modify; QA/DEV=Read"
Write-Host " - repositories: QA/Admin=Modify; DEV=Read"
Write-Host " - changes: DEV=Create/Modify próprios; QA/Admin=Modify"
Write-Host " - locks: DEV/QA/Admin=Modify"
Write-Host " - audit: preferir Create/Append sem Delete quando a política NTFS permitir"
Write-Host " - pasta oficial de produção: QA/Admin=Modify; DEV=Read/sem escrita"
Write-Host ""
Write-Host "Grupos informados: DEV=$DevGroup | QA=$QaGroup | ADMIN=$AdminGroup"
Write-Host "Nenhuma ACL foi alterada automaticamente por segurança."
