using PrintTool.Ui.Shared.Mvvm;

namespace PrintTool.Host.UI.ViewModels;

/// <summary>
/// Uma impressora instalada localmente, com o estado (ligado/desligado) de compartilhamento
/// na rede. A troca do toggle já grava <c>sharedprinters.json</c> na hora — sem botão "Salvar"
/// separado, pra combinar com o resto do app (abrir, mudar, fechar).
/// </summary>
public sealed class LocalPrinterItem : ObservableObject
{
    private readonly Action<LocalPrinterItem> _onSharedChanged;
    private bool _isShared;

    public LocalPrinterItem(string name, bool isShared, Action<LocalPrinterItem> onSharedChanged)
    {
        Name = name;
        _isShared = isShared;
        _onSharedChanged = onSharedChanged;
    }

    public string Name { get; }

    public bool IsShared
    {
        get => _isShared;
        set
        {
            if (SetProperty(ref _isShared, value))
            {
                _onSharedChanged(this);
            }
        }
    }
}
