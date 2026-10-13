using System.Windows;
using PrintTool.Ui.Shared.Theme;

namespace PrintTool.Client.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeLoader.Apply(this);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    // Rede de segurança: sem isso, qualquer exceção não tratada na thread de UI fecha o app
    // em silêncio, sem nenhuma mensagem — o que torna impossível diagnosticar o que falhou.
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "Erro inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
