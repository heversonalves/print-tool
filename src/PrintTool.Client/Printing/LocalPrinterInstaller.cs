using System.ComponentModel;
using System.Diagnostics;

namespace PrintTool.Client.Printing;

/// <summary>Resultado de uma tentativa de instalar/atualizar a impressora local no Windows.</summary>
public sealed record PrinterInstallResult(bool Success, string? ErrorMessage)
{
    public static PrinterInstallResult Ok() => new(true, null);

    public static PrinterInstallResult Failure(string message) => new(false, message);
}

/// <summary>
/// Cria a porta e o objeto de impressora no Windows apontando para o loopback do
/// <see cref="Loopback.PrinterBridge"/> (127.0.0.1:porta), substituindo os passos manuais no
/// Painel de Controle (Adicionar impressora → porta TCP/IP padrão → 127.0.0.1:porta →
/// desmarcar SNMP). Usa os cmdlets <c>Add-PrinterPort</c>/<c>Add-Printer</c> via PowerShell
/// em vez de montar a struct binária do <c>AddPortEx</c> à mão — é o mesmo caminho suportado
/// oficialmente, só automatizado, e evita depender de uma API Win32 pouco documentada e
/// instável entre versões do Windows.
/// </summary>
public static class LocalPrinterInstaller
{
    /// <summary>
    /// Monta o script que cria a porta (se ainda não existir) e a impressora (se ainda não
    /// existir) — idempotente, então rodar de novo numa máquina já configurada não duplica nada.
    /// Função pura (sem I/O), para dar pra testar sem depender do Windows de verdade.
    /// </summary>
    public static string BuildInstallScript(string printerName, int localPort, string driverName)
    {
        string portName = PortNameFor(printerName);

        return $$"""
            $ErrorActionPreference = 'Stop'
            if (-not (Get-PrinterPort -Name '{{Escape(portName)}}' -ErrorAction SilentlyContinue)) {
                Add-PrinterPort -Name '{{Escape(portName)}}' -PrinterHostAddress '127.0.0.1' -PortNumber {{localPort}}
            }
            if (-not (Get-Printer -Name '{{Escape(printerName)}}' -ErrorAction SilentlyContinue)) {
                Add-Printer -Name '{{Escape(printerName)}}' -PortName '{{Escape(portName)}}' -DriverName '{{Escape(driverName)}}'
            }
            """;
    }

    /// <summary>Nome da porta local criada no Windows para uma impressora remota pareada.</summary>
    public static string PortNameFor(string printerName) => $"PrintTool_{Sanitize(printerName)}";

    /// <summary>
    /// Nomes dos drivers de impressora já instalados no Windows, para a pessoa escolher qual
    /// associar à impressora nova — não precisa de elevação, só lista o que já existe.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -Command \"(Get-PrinterDriver).Name\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return Array.Empty<string>();
        }

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    /// <summary>
    /// Dentre os drivers instalados, sugere o que mais provavelmente corresponde à impressora
    /// remota — o nome exatamente igual ao compartilhado pelo Host é o caso comum, já que o
    /// mesmo modelo costuma aparecer com o mesmo nome de driver em instalações padrão do Windows.
    /// </summary>
    public static string? SuggestDriverFor(string printerName, IReadOnlyList<string> installedDrivers)
    {
        string? exact = installedDrivers.FirstOrDefault(d => string.Equals(d, printerName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return installedDrivers.FirstOrDefault(d => d.Contains(printerName, StringComparison.OrdinalIgnoreCase)
            || printerName.Contains(d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Roda o script de instalação elevado (precisa de administrador para mexer no spooler) —
    /// isola o pedido de elevação (prompt do UAC) nesse único momento, em vez de exigir que o
    /// app inteiro rode elevado o tempo todo só por causa deste passo.
    /// </summary>
    public static async Task<PrinterInstallResult> RunElevatedAsync(string script, CancellationToken cancellationToken)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"printtool-install-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return PrinterInstallResult.Failure("Não foi possível iniciar o instalador da impressora.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return process.ExitCode == 0
                ? PrinterInstallResult.Ok()
                : PrinterInstallResult.Failure($"O instalador terminou com código de erro {process.ExitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: a pessoa recusou o prompt de elevação do UAC.
            return PrinterInstallResult.Failure("Instalação cancelada: é preciso aprovar a elevação de administrador.");
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }
        return value.Replace(' ', '_');
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
