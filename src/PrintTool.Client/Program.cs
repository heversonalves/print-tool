using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintTool.Client;
using PrintTool.Client.Discovery;
using PrintTool.Client.Loopback;
using PrintTool.Client.Security;
using PrintTool.Common.Discovery;

string securityDirectory = Path.Combine(AppContext.BaseDirectory, "security");

if (args.Length > 0 && await TryRunAdminCommandAsync(args, securityDirectory))
{
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "PrintTool.Client");

// Rodando como serviço não há console pra onde escrever; sem isso, os logs da aplicação
// (fora o ciclo de vida do serviço, que o Windows já registra sozinho) não ficavam em lugar nenhum.
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Logging.AddEventLog(settings => settings.SourceName = "PrintTool.Client");
}

string mappingConfigPath = Path.Combine(AppContext.BaseDirectory, "printers.json");
ClientPrinterMappingConfig mappingConfig = ClientPrinterMappingConfig.LoadOrCreate(mappingConfigPath);
string spoolDirectory = Path.Combine(AppContext.BaseDirectory, "spool");

builder.Services.AddSingleton(mappingConfig);
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection("Discovery"));
builder.Services.AddSingleton<IDiscoveryClient, UdpDiscoveryClient>();
builder.Services.AddSingleton<DiscoveredHostTable>();
builder.Services.AddSingleton(ClientIdentity.LoadOrCreate(Path.Combine(securityDirectory, "client-identity.json")));
builder.Services.AddSingleton(new HostTokenStore(Path.Combine(securityDirectory, "tokens.json")));
builder.Services.AddHostedService(sp => new LoopbackServer(
    mappingConfig,
    sp.GetRequiredService<DiscoveredHostTable>(),
    sp.GetRequiredService<ClientIdentity>(),
    sp.GetRequiredService<HostTokenStore>(),
    sp.GetRequiredService<ILoggerFactory>(),
    spoolDirectory));
builder.Services.AddHostedService<DiscoveryRefreshService>();

var host = builder.Build();
host.Run();

/// <summary>
/// Trata o subcomando de CLI <c>pair &lt;impressora&gt;</c>, que não sobe o serviço: pede o
/// código de 6 dígitos no console, pareia com o Host que anuncia essa impressora e grava o
/// token localmente. Devolve <c>true</c> se tratou o comando.
/// </summary>
static async Task<bool> TryRunAdminCommandAsync(string[] args, string securityDirectory)
{
    if (args[0] != "pair")
    {
        return false;
    }

    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.WriteLine("Uso: PrintTool.Client.exe pair <nome-da-impressora>");
        return true;
    }

    string printerName = args[1];

    Console.Write("Código de 6 dígitos do autenticador: ");
    string? code = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(code))
    {
        Console.WriteLine("Nenhum código informado. Pareamento cancelado.");
        return true;
    }

    IConfiguration configuration = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
        .Build();
    var discoveryOptions = Options.Create(configuration.GetSection("Discovery").Get<DiscoveryOptions>() ?? new DiscoveryOptions());

    var discoveryClient = new UdpDiscoveryClient(discoveryOptions, NullLogger<UdpDiscoveryClient>.Instance);
    var hostTable = new DiscoveredHostTable(discoveryClient, NullLogger<DiscoveredHostTable>.Instance);
    ClientIdentity clientIdentity = ClientIdentity.LoadOrCreate(Path.Combine(securityDirectory, "client-identity.json"));
    var tokenStore = new HostTokenStore(Path.Combine(securityDirectory, "tokens.json"));
    var pairingClient = new PairingClient(hostTable, clientIdentity, tokenStore);

    Console.WriteLine($"Procurando Host para a impressora '{printerName}' na rede...");
    PairingOutcome outcome = await pairingClient.PairAsync(printerName, code.Trim(), CancellationToken.None);

    Console.WriteLine(outcome.Success
        ? "Pareamento aprovado. Esta máquina já pode imprimir sem pedir código de novo."
        : $"Pareamento falhou: {outcome.ErrorMessage}");

    return true;
}
