#Requires -RunAsAdministrator
param(
    [string]$ServiceName = "PrintTool.Client"
)

if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    Write-Host "Serviço '$ServiceName' não está instalado."
    return
}

Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
sc.exe delete $ServiceName

Write-Host "Serviço '$ServiceName' removido."
