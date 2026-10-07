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
Write-Host "Use o app 'PrintTool Client' (atalho criado abaixo) para conectar e parear impressoras — ou edite printers.json na pasta do serviço manualmente."

# Atalho no Menu Iniciar pro app de administração (ver impressoras na rede, conectar e parear
# uma máquina nova) — o dia a dia passa a ser por ali, sem precisar de terminal nem editar JSON.
# PrintTool.Client.UI publica direto nesta mesma pasta (ver PublishDir no .csproj dele), pra
# ler/escrever exatamente os mesmos security/printers.json do serviço.
$uiExePath = Join-Path $PublishDir "PrintTool.Client.UI.exe"
if (Test-Path $uiExePath) {
    $startMenuPrograms = Join-Path ([Environment]::GetFolderPath("CommonStartMenu")) "Programs"
    $shortcutPath = Join-Path $startMenuPrograms "PrintTool Client.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $uiExePath
    $shortcut.WorkingDirectory = $PublishDir
    $shortcut.Description = "Conectar esta máquina a impressoras compartilhadas na rede (Print Tool - Client)"
    $shortcut.Save()
    Write-Host "Atalho criado no Menu Iniciar: 'PrintTool Client'."
}
else {
    Write-Host "Aviso: '$uiExePath' não encontrado — atalho do Menu Iniciar não criado. Publique com: dotnet publish src\PrintTool.Client.UI -c Release"
}
