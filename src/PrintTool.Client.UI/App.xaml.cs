using System.Windows;
using PrintTool.Ui.Shared.Theme;

namespace PrintTool.Client.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeLoader.Apply(this);
        base.OnStartup(e);
    }
}
