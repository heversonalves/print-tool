#Requires -RunAsAdministrator
<#
    Instala o PrintTool.Host como Windows Service.
    Publique antes: dotnet publish src\PrintTool.Host -c Release
#>
param(
    [string]$PublishDir = "$PSScriptRoot\..\src\PrintTool.Host\bin\Release\net8.0-windows\publish",
    [string]$ServiceName = "PrintTool.Host"
)

$exePath = Join-Path $PublishDir "PrintTool.Host.exe"
if (-not (Test-Path $exePath)) {
    throw "Executável não encontrado em '$exePath'. Publique o projeto antes com: dotnet publish src\PrintTool.Host -c Release"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Já existe um serviço '$ServiceName'. Remova-o antes com uninstall-host.ps1."
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName $exePath `
    -DisplayName "Print Tool - Host" `
    -StartupType Automatic `
    -Description "Compartilha impressoras USB locais na rede (Print Tool)."

Write-Host "Serviço '$ServiceName' instalado. Inicie com: Start-Service $ServiceName"
