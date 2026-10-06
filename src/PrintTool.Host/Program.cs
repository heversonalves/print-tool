using Microsoft.Extensions.Hosting.WindowsServices;
using PrintTool.Common.Discovery;
using PrintTool.Host.Discovery;
using PrintTool.Host.Network;
using PrintTool.Host.Printers;
using PrintTool.Host.Security;

string securityDirectory = Path.Combine(AppContext.BaseDirectory, "security");

if (args.Length > 0 && TryRunAdminCommand(args, securityDirectory))
{
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "PrintTool.Host");

// Rodando como serviço não há console pra onde escrever; sem isso, os logs da aplicação
// (fora o ciclo de vida do serviço, que o Windows já registra sozinho) não ficavam em lugar nenhum.
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Logging.AddEventLog(settings => settings.SourceName = "PrintTool.Host");
}

builder.Services.Configure<PrintServerOptions>(builder.Configuration.GetSection("PrintServer"));
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection("Discovery"));

string sharedPrintersConfigPath = Path.Combine(AppContext.BaseDirectory, "sharedprinters.json");
builder.Services.AddSingleton(SharedPrintersConfig.LoadOrCreate(sharedPrintersConfigPath));

builder.Services.AddSingleton(HostIdentity.LoadOrCreate(securityDirectory));
builder.Services.AddSingleton(TotpSecretStore.LoadOrCreate(Path.Combine(securityDirectory, "totp-secret.json")));
builder.Services.AddSingleton(ClientTokenStore.LoadOrCreate(Path.Combine(securityDirectory, "tokens.json")));

builder.Services.AddSingleton<IPrinterManager, WindowsPrinterManager>();
builder.Services.AddSingleton<PrintServer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PrintServer>());
builder.Services.AddHostedService<UdpDiscoveryAnnouncer>();

var host = builder.Build();
host.Run();

/// <summary>
/// Trata os subcomandos administrativos de CLI, que não sobem o serviço: <c>show-totp</c>
/// (reexibe a URI otpauth e o caminho do QR code) e <c>revoke-client &lt;clientId&gt;</c>
/// (revoga o acesso de uma máquina pareada). Devolve <c>true</c> se tratou o comando.
/// </summary>
static bool TryRunAdminCommand(string[] args, string securityDirectory)
{
    switch (args[0])
    {
        case "show-totp":
        {
            TotpSecretStore totpSecretStore = TotpSecretStore.LoadOrCreate(Path.Combine(securityDirectory, "totp-secret.json"));
            string otpAuthUri = QrCodeWriter.BuildOtpAuthUri(Environment.MachineName, totpSecretStore.SecretBase32);
            string pngPath = Path.Combine(securityDirectory, "totp-qrcode.png");
            QrCodeWriter.WritePng(otpAuthUri, pngPath);

            Console.WriteLine("Escaneie o QR code abaixo no app autenticador (Google/Microsoft Authenticator, Authy):");
            Console.WriteLine($"  Arquivo: {pngPath}");
            Console.WriteLine();
            Console.WriteLine("Se preferir digitar manualmente ('Inserir código de configuração'):");
            Console.WriteLine($"  Segredo: {totpSecretStore.SecretBase32}");
            Console.WriteLine($"  URI:     {otpAuthUri}");
            return true;
        }

        case "revoke-client":
        {
            if (args.Length < 2 || !Guid.TryParse(args[1], out Guid clientId))
            {
                Console.WriteLine("Uso: PrintTool.Host.exe revoke-client <clientId>");
                return true;
            }

            ClientTokenStore clientTokenStore = ClientTokenStore.LoadOrCreate(Path.Combine(securityDirectory, "tokens.json"));
            bool revoked = clientTokenStore.Revoke(clientId);
            Console.WriteLine(revoked
                ? $"Acesso de {clientId} revogado. A próxima impressão dessa máquina vai exigir novo pareamento via TOTP."
                : $"Nenhum cliente pareado encontrado com ClientId {clientId}.");
            return true;
        }

        case "list-clients":
        {
            ClientTokenStore clientTokenStore = ClientTokenStore.LoadOrCreate(Path.Combine(securityDirectory, "tokens.json"));
            IReadOnlyList<ClientTokenEntry> clients = clientTokenStore.ListClients();
            if (clients.Count == 0)
            {
                Console.WriteLine("Nenhuma máquina pareada ainda.");
                return true;
            }

            foreach (ClientTokenEntry client in clients)
            {
                Console.WriteLine($"  [{(client.Revoked ? "revogado" : "ativo")}] {client.ClientId}  {client.DisplayName}  pareado em {client.PairedAtUtc:u}");
            }
            return true;
        }

        default:
            return false;
    }
}
