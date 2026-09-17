#Requires -RunAsAdministrator
<#
    Instala o PrintTool.Client como Windows Service.
    Publique antes: dotnet publish src\PrintTool.Client -c Release
#>
param(
    [string]$PublishDir = "$PSScriptRoot\..\src\PrintTool.Client\bin\Release\net8.0-windows\publish",
    [string]$ServiceName = "PrintTool.Client"
)

$exePath = Join-Path $PublishDir "PrintTool.Client.exe"
if (-not (Test-Path $exePath)) {
    throw "Executável não encontrado em '$exePath'. Publique o projeto antes com: dotnet publish src\PrintTool.Client -c Release"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Já existe um serviço '$ServiceName'. Remova-o antes com uninstall-client.ps1."
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName $exePath `
    -DisplayName "Print Tool - Client" `
    -StartupType Automatic `
    -Description "Encaminha jobs de impressão para o Host do Print Tool na rede local."

Write-Host "Serviço '$ServiceName' instalado. Inicie com: Start-Service $ServiceName"
Write-Host "Antes de iniciar, edite printers.json na pasta do serviço com as portas locais e impressoras remotas."
