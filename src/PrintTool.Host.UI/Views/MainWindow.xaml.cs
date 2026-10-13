using System.Windows;
using PrintTool.Ui.Shared.Interop;
using PrintTool.Ui.Shared.Theme;

namespace PrintTool.Host.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowBackdrop.Apply(this, SystemTheme.IsDarkMode());
    }
}
