using System.ComponentModel;
using System.Windows;
using PrintTool.Client.UI.ViewModels;
using PrintTool.Ui.Shared.Interop;
using PrintTool.Ui.Shared.Theme;

namespace PrintTool.Client.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (DataContext is INotifyPropertyChanged viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // O campo de código é feito de 6 caixas separadas — sem isso, nada move o foco pra lá
    // quando a tela de pareamento abre, e o usuário precisa achar e clicar numa caixa específica
    // antes de conseguir digitar.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPairingSectionSelected) &&
            sender is MainViewModel { IsPairingSectionSelected: true })
        {
            PasscodeInput.FocusFirst();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowBackdrop.Apply(this, SystemTheme.IsDarkMode());
    }
}
