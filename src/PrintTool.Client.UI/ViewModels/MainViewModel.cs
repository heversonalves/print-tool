using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintTool.Client;
using PrintTool.Client.Discovery;
using PrintTool.Client.Security;
using PrintTool.Common.Discovery;
using PrintTool.Ui.Shared.Mvvm;

namespace PrintTool.Client.UI.ViewModels;

/// <summary>
/// Estado e ações das três seções da janela: impressoras na rede (descoberta), conectar
/// (escolher porta + parear via código de 6 dígitos) e impressoras configuradas (status +
/// revogar/remover). Lê e escreve exatamente os mesmos arquivos que os comandos de CLI
/// (<c>pair</c>) e o serviço em segundo plano já usam — nenhuma lógica nova de rede.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly string _mappingConfigPath;
    private readonly DiscoveredHostTable _hostTable;
    private readonly HostTokenStore _tokenStore;
    private readonly PairingClient _pairingClient;

    private bool _isDiscoverySectionSelected = true;
    private bool _isPairingSectionSelected;
    private bool _isConfiguredSectionSelected;
    private string _selectedPrinterName = string.Empty;
    private string _code = string.Empty;
    private string _pairingStatusMessage = string.Empty;
    private bool _pairingStatusIsError;

    public MainViewModel()
    {
        _mappingConfigPath = Path.Combine(AppContext.BaseDirectory, "printers.json");
        string securityDirectory = Path.Combine(AppContext.BaseDirectory, "security");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .Build();
        var discoveryOptions = Options.Create(configuration.GetSection("Discovery").Get<DiscoveryOptions>() ?? new DiscoveryOptions());

        var discoveryClient = new UdpDiscoveryClient(discoveryOptions, NullLogger<UdpDiscoveryClient>.Instance);
        _hostTable = new DiscoveredHostTable(discoveryClient, NullLogger<DiscoveredHostTable>.Instance);
        ClientIdentity clientIdentity = ClientIdentity.LoadOrCreate(Path.Combine(securityDirectory, "client-identity.json"));
        _tokenStore = new HostTokenStore(Path.Combine(securityDirectory, "tokens.json"));
        _pairingClient = new PairingClient(_hostTable, clientIdentity, _tokenStore);

        DiscoveredPrinters = new ObservableCollection<DiscoveredPrinterItem>();
        ConfiguredPrinters = new ObservableCollection<ConfiguredPrinterItem>();

        SelectDiscoverySectionCommand = new RelayCommand(() => SetSelectedSection(discovery: true));
        SelectPairingSectionCommand = new RelayCommand(() => SetSelectedSection(pairing: true));
        SelectConfiguredSectionCommand = new RelayCommand(() => SetSelectedSection(configured: true));

        RefreshDiscoveryCommand = new AsyncRelayCommand(RefreshDiscoveryAsync);
        ConnectToDiscoveredCommand = new RelayCommand<DiscoveredPrinterItem>(StartPairing);
        RefreshConfiguredCommand = new RelayCommand(ReloadConfiguredPrinters);
        PairConfiguredCommand = new RelayCommand<ConfiguredPrinterItem>(item => StartPairingByName(item?.RemotePrinterName));
        RemoveConfiguredCommand = new RelayCommand<ConfiguredPrinterItem>(RemoveMapping);
        ConnectCommand = new AsyncRelayCommand(PairAsync);

        ReloadConfiguredPrinters();
        RefreshDiscoveryCommand.Execute(null);
    }

    public ObservableCollection<DiscoveredPrinterItem> DiscoveredPrinters { get; }

    public ObservableCollection<ConfiguredPrinterItem> ConfiguredPrinters { get; }

    public bool IsDiscoverySectionSelected
    {
        get => _isDiscoverySectionSelected;
        set => SetProperty(ref _isDiscoverySectionSelected, value);
    }

    public bool IsPairingSectionSelected
    {
        get => _isPairingSectionSelected;
        set => SetProperty(ref _isPairingSectionSelected, value);
    }

    public bool IsConfiguredSectionSelected
    {
        get => _isConfiguredSectionSelected;
        set => SetProperty(ref _isConfiguredSectionSelected, value);
    }

    public string SelectedPrinterName
    {
        get => _selectedPrinterName;
        set => SetProperty(ref _selectedPrinterName, value);
    }

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    public string PairingStatusMessage
    {
        get => _pairingStatusMessage;
        private set => SetProperty(ref _pairingStatusMessage, value);
    }

    public bool PairingStatusIsError
    {
        get => _pairingStatusIsError;
        private set => SetProperty(ref _pairingStatusIsError, value);
    }

    public RelayCommand SelectDiscoverySectionCommand { get; }

    public RelayCommand SelectPairingSectionCommand { get; }

    public RelayCommand SelectConfiguredSectionCommand { get; }

    public AsyncRelayCommand RefreshDiscoveryCommand { get; }

    public RelayCommand<DiscoveredPrinterItem> ConnectToDiscoveredCommand { get; }

    public RelayCommand RefreshConfiguredCommand { get; }

    public RelayCommand<ConfiguredPrinterItem> PairConfiguredCommand { get; }

    public RelayCommand<ConfiguredPrinterItem> RemoveConfiguredCommand { get; }

    public AsyncRelayCommand ConnectCommand { get; }

    private void SetSelectedSection(bool discovery = false, bool pairing = false, bool configured = false)
    {
        IsDiscoverySectionSelected = discovery;
        IsPairingSectionSelected = pairing;
        IsConfiguredSectionSelected = configured;
    }

    private async Task RefreshDiscoveryAsync()
    {
        await _hostTable.RefreshAsync(ProbeTimeout, CancellationToken.None).ConfigureAwait(true);

        ClientPrinterMappingConfig mappingConfig = ClientPrinterMappingConfig.LoadOrCreate(_mappingConfigPath);
        var configuredNames = new HashSet<string>(
            mappingConfig.Mappings.Select(m => m.RemotePrinterName),
            StringComparer.OrdinalIgnoreCase);

        DiscoveredPrinters.Clear();
        foreach (KeyValuePair<string, ResolvedHost> entry in _hostTable.GetAll().OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            bool isConfigured = configuredNames.Contains(entry.Key);
            DiscoveredPrinters.Add(new DiscoveredPrinterItem(entry.Key, entry.Value.HostName, entry.Value.Address.ToString(), isConfigured));
        }
    }

    private void StartPairing(DiscoveredPrinterItem? item)
    {
        if (item is null)
        {
            return;
        }

        StartPairingByName(item.PrinterName);
    }

    private void StartPairingByName(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return;
        }

        SelectedPrinterName = printerName;
        Code = string.Empty;
        PairingStatusMessage = string.Empty;
        SetSelectedSection(pairing: true);
    }

    private async Task PairAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPrinterName))
        {
            PairingStatusMessage = "Informe o nome da impressora.";
            PairingStatusIsError = true;
            return;
        }

        if (Code.Length != 6)
        {
            PairingStatusMessage = "Digite o código de 6 dígitos do app autenticador.";
            PairingStatusIsError = true;
            return;
        }

        PairingStatusMessage = $"Procurando Host para '{SelectedPrinterName}' na rede...";
        PairingStatusIsError = false;

        PairingOutcome outcome = await _pairingClient.PairAsync(SelectedPrinterName, Code, CancellationToken.None).ConfigureAwait(true);

        if (!outcome.Success)
        {
            PairingStatusMessage = outcome.ErrorMessage ?? "Pareamento não aprovado pelo Host.";
            PairingStatusIsError = true;
            Code = string.Empty;
            return;
        }

        SaveOrUpdateMapping(SelectedPrinterName);

        PairingStatusMessage = $"'{SelectedPrinterName}' pareada com sucesso. Esta máquina já pode imprimir.";
        PairingStatusIsError = false;
        Code = string.Empty;

        ReloadConfiguredPrinters();
        SetSelectedSection(configured: true);
    }

    private void SaveOrUpdateMapping(string printerName)
    {
        ClientPrinterMappingConfig mappingConfig = ClientPrinterMappingConfig.LoadOrCreate(_mappingConfigPath);

        if (mappingConfig.Mappings.Any(m => string.Equals(m.RemotePrinterName, printerName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        int localPort = GetFreeLocalPort();
        mappingConfig.Mappings.Add(new ClientPrinterMapping(localPort, printerName));
        mappingConfig.Save(_mappingConfigPath);
    }

    private void RemoveMapping(ConfiguredPrinterItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClientPrinterMappingConfig mappingConfig = ClientPrinterMappingConfig.LoadOrCreate(_mappingConfigPath);
        mappingConfig.Mappings.RemoveAll(m => string.Equals(m.RemotePrinterName, item.RemotePrinterName, StringComparison.OrdinalIgnoreCase));
        mappingConfig.Save(_mappingConfigPath);

        ReloadConfiguredPrinters();
    }

    private void ReloadConfiguredPrinters()
    {
        ClientPrinterMappingConfig mappingConfig = ClientPrinterMappingConfig.LoadOrCreate(_mappingConfigPath);

        ConfiguredPrinters.Clear();
        foreach (ClientPrinterMapping mapping in mappingConfig.Mappings.OrderBy(m => m.RemotePrinterName, StringComparer.OrdinalIgnoreCase))
        {
            bool isPaired = false;
            string hostStatusText = "Host não encontrado na rede";

            if (_hostTable.TryResolve(mapping.RemotePrinterName, out ResolvedHost? host) && host is not null)
            {
                isPaired = _tokenStore.TryGet(host.HostId) is not null;
                hostStatusText = $"Host: {host.HostName}";
            }

            ConfiguredPrinters.Add(new ConfiguredPrinterItem(mapping.RemotePrinterName, mapping.LocalPort, isPaired, hostStatusText));
        }
    }

    private static int GetFreeLocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
