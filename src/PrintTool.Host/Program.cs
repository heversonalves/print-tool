using PrintTool.Host.Discovery;
using PrintTool.Host.Network;
using PrintTool.Host.Printers;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "PrintTool.Host");

builder.Services.Configure<PrintServerOptions>(builder.Configuration.GetSection("PrintServer"));

string sharedPrintersConfigPath = Path.Combine(AppContext.BaseDirectory, "sharedprinters.json");
builder.Services.AddSingleton(SharedPrintersConfig.LoadOrCreate(sharedPrintersConfigPath));

builder.Services.AddSingleton<IPrinterManager, WindowsPrinterManager>();
builder.Services.AddSingleton<PrintServer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PrintServer>());
builder.Services.AddHostedService<UdpDiscoveryAnnouncer>();

var host = builder.Build();
host.Run();
