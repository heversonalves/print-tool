using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrintTool.Ui.Shared.Mvvm;

/// <summary>
/// Base mínima de MVVM (sem dependência de pacote externo — o escopo das telas aqui é
/// pequeno o suficiente pra não justificar um framework inteiro).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
