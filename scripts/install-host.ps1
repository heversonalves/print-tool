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

# Atalho no Menu Iniciar pro app de administração (compartilhar impressora, QR de pareamento,
# revogar máquinas) — o dia a dia passa a ser por ali, sem precisar de terminal.
# PrintTool.Host.UI publica direto nesta mesma pasta (ver PublishDir no .csproj dele), pra
# ler/escrever exatamente os mesmos security/sharedprinters.json do serviço.
$uiExePath = Join-Path $PublishDir "PrintTool.Host.UI.exe"
if (Test-Path $uiExePath) {
    $startMenuPrograms = Join-Path ([Environment]::GetFolderPath("CommonStartMenu")) "Programs"
    $shortcutPath = Join-Path $startMenuPrograms "PrintTool Host.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $uiExePath
    $shortcut.WorkingDirectory = $PublishDir
    $shortcut.Description = "Administrar impressoras compartilhadas e pareamento (Print Tool - Host)"
    $shortcut.Save()
    Write-Host "Atalho criado no Menu Iniciar: 'PrintTool Host'."
}
else {
    Write-Host "Aviso: '$uiExePath' não encontrado — atalho do Menu Iniciar não criado. Publique com: dotnet publish src\PrintTool.Host.UI -c Release"
}
