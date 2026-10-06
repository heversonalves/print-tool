#Requires -RunAsAdministrator
param(
    [string]$ServiceName = "PrintTool.Host"
)

if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    Write-Host "Serviço '$ServiceName' não está instalado."
    return
}

Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
sc.exe delete $ServiceName

Write-Host "Serviço '$ServiceName' removido."

$shortcutPath = Join-Path ([Environment]::GetFolderPath("CommonStartMenu")) "Programs\PrintTool Host.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath
    Write-Host "Atalho do Menu Iniciar removido."
}
