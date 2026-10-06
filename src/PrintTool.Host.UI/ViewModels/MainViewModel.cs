using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PrintTool.Host.Printers;
using PrintTool.Host.Security;
using PrintTool.Ui.Shared.Mvvm;

namespace PrintTool.Host.UI.ViewModels;

/// <summary>
/// Estado e ações das três seções da janela: impressoras locais (compartilhar/não),
/// pareamento (QR code + segredo) e máquinas pareadas (revogar). Lê e escreve exatamente os
/// mesmos arquivos que o serviço Windows e os comandos de CLI já usam — nenhuma lógica nova,
/// só a casca gráfica em cima do que já existia.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly string _sharedPrintersConfigPath;
    private readonly IPrinterManager _printerManager;
    private readonly ClientTokenStore _clientTokenStore;

    private bool _isPrintersSectionSelected = true;
    private bool _isPairingSectionSelected;
    private bool _isDevicesSectionSelected;
    private BitmapImage? _qrCodeImage;
    private string _statusMessage = string.Empty;

    public MainViewModel()
    {
        string securityDirectory = Path.Combine(AppContext.BaseDirectory, "security");
        _sharedPrintersConfigPath = Path.Combine(AppContext.BaseDirectory, "sharedprinters.json");

        HostName = Environment.MachineName;
        _printerManager = new WindowsPrinterManager();
        _clientTokenStore = ClientTokenStore.LoadOrCreate(Path.Combine(securityDirectory, "tokens.json"));

        TotpSecretStore totpSecretStore = TotpSecretStore.LoadOrCreate(Path.Combine(securityDirectory, "totp-secret.json"));
        SecretBase32 = totpSecretStore.SecretBase32;
        string otpAuthUri = QrCodeWriter.BuildOtpAuthUri(HostName, SecretBase32);
        QrCodeImage = DecodePng(QrCodeWriter.GeneratePng(otpAuthUri));

        LocalPrinters = new ObservableCollection<LocalPrinterItem>();
        PairedClients = new ObservableCollection<PairedClientItem>();

        SelectPrintersSectionCommand = new RelayCommand(() => SetSelectedSection(printers: true));
        SelectPairingSectionCommand = new RelayCommand(() => SetSelectedSection(pairing: true));
        SelectDevicesSectionCommand = new RelayCommand(() => SetSelectedSection(devices: true));
        CopySecretCommand = new RelayCommand(() => Clipboard.SetText(SecretBase32));
        RefreshDevicesCommand = new RelayCommand(ReloadPairedClients);
        RevokeCommand = new RelayCommand<Guid>(RevokeClient);

        ReloadLocalPrinters();
        ReloadPairedClients();
    }

    public string HostName { get; }

    public string SecretBase32 { get; }

    public BitmapImage? QrCodeImage
    {
        get => _qrCodeImage;
        private set => SetProperty(ref _qrCodeImage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<LocalPrinterItem> LocalPrinters { get; }

    public ObservableCollection<PairedClientItem> PairedClients { get; }

    public bool IsPrintersSectionSelected
    {
        get => _isPrintersSectionSelected;
        set => SetProperty(ref _isPrintersSectionSelected, value);
    }

    public bool IsPairingSectionSelected
    {
        get => _isPairingSectionSelected;
        set => SetProperty(ref _isPairingSectionSelected, value);
    }

    public bool IsDevicesSectionSelected
    {
        get => _isDevicesSectionSelected;
        set => SetProperty(ref _isDevicesSectionSelected, value);
    }

    public RelayCommand SelectPrintersSectionCommand { get; }

    public RelayCommand SelectPairingSectionCommand { get; }

    public RelayCommand SelectDevicesSectionCommand { get; }

    public RelayCommand CopySecretCommand { get; }

    public RelayCommand RefreshDevicesCommand { get; }

    public RelayCommand<Guid> RevokeCommand { get; }

    private void SetSelectedSection(bool printers = false, bool pairing = false, bool devices = false)
    {
        IsPrintersSectionSelected = printers;
        IsPairingSectionSelected = pairing;
        IsDevicesSectionSelected = devices;
    }

    private void ReloadLocalPrinters()
    {
        SharedPrintersConfig sharedConfig = SharedPrintersConfig.LoadOrCreate(_sharedPrintersConfigPath);

        LocalPrinters.Clear();
        foreach (string name in _printerManager.GetLocalPrinterNames())
        {
            bool isShared = sharedConfig.IsShared(name);
            LocalPrinters.Add(new LocalPrinterItem(name, isShared, OnPrinterSharedChanged));
        }
    }

    private void OnPrinterSharedChanged(LocalPrinterItem _)
    {
        var sharedNames = LocalPrinters.Where(p => p.IsShared).Select(p => p.Name).ToList();
        var config = new SharedPrintersConfig { SharedPrinterNames = sharedNames };
        config.Save(_sharedPrintersConfigPath);
        StatusMessage = "Alterações salvas.";
    }

    private void ReloadPairedClients()
    {
        PairedClients.Clear();
        foreach (ClientTokenEntry entry in _clientTokenStore.ListClients().OrderByDescending(e => e.PairedAtUtc))
        {
            PairedClients.Add(new PairedClientItem(entry));
        }
    }

    private void RevokeClient(Guid? clientId)
    {
        if (clientId is null)
        {
            return;
        }

        if (_clientTokenStore.Revoke(clientId.Value))
        {
            PairedClientItem? item = PairedClients.FirstOrDefault(c => c.ClientId == clientId.Value);
            if (item is not null)
            {
                item.IsRevoked = true;
            }
        }
    }

    private static BitmapImage DecodePng(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
