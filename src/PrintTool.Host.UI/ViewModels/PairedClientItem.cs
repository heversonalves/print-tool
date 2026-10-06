using PrintTool.Host.Security;
using PrintTool.Ui.Shared.Mvvm;

namespace PrintTool.Host.UI.ViewModels;

/// <summary>Uma máquina Client pareada com este Host, pronta pra exibição na lista.</summary>
public sealed class PairedClientItem : ObservableObject
{
    private bool _isRevoked;

    public PairedClientItem(ClientTokenEntry entry)
    {
        ClientId = entry.ClientId;
        DisplayName = entry.DisplayName;
        PairedAtText = entry.PairedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        _isRevoked = entry.Revoked;
    }

    public Guid ClientId { get; }

    public string DisplayName { get; }

    public string PairedAtText { get; }

    public bool IsRevoked
    {
        get => _isRevoked;
        set
        {
            if (SetProperty(ref _isRevoked, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanRevoke));
            }
        }
    }

    public string StatusText => IsRevoked ? "Revogado" : "Ativo";

    public bool CanRevoke => !IsRevoked;
}
