using PrintTool.Client;
using PrintTool.Client.Discovery;
using PrintTool.Client.Loopback;
using PrintTool.Common.Discovery;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "PrintTool.Client");

string mappingConfigPath = Path.Combine(AppContext.BaseDirectory, "printers.json");
ClientPrinterMappingConfig mappingConfig = ClientPrinterMappingConfig.LoadOrCreate(mappingConfigPath);
string spoolDirectory = Path.Combine(AppContext.BaseDirectory, "spool");

builder.Services.AddSingleton(mappingConfig);
builder.Services.AddSingleton<IDiscoveryClient, UdpDiscoveryClient>();
builder.Services.AddSingleton<DiscoveredHostTable>();
builder.Services.AddHostedService(sp => new LoopbackServer(
    mappingConfig,
    sp.GetRequiredService<DiscoveredHostTable>(),
    sp.GetRequiredService<ILoggerFactory>(),
    spoolDirectory));

var host = builder.Build();
host.Run();
