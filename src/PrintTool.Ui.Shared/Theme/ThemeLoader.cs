using System.Windows;

namespace PrintTool.Ui.Shared.Theme;

/// <summary>
/// Carrega o design system (cores + tipografia + estilos + ícones) nos recursos da
/// aplicação. Chamar uma vez, no <c>OnStartup</c> do <c>App.xaml.cs</c> de cada app.
/// </summary>
public static class ThemeLoader
{
    public static void Apply(Application application)
    {
        string colorsUri = SystemTheme.IsDarkMode()
            ? "pack://application:,,,/PrintTool.Ui.Shared;component/Theme/Colors.Dark.xaml"
            : "pack://application:,,,/PrintTool.Ui.Shared;component/Theme/Colors.Light.xaml";

        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(colorsUri) });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PrintTool.Ui.Shared;component/Theme/DesignSystem.xaml"),
        });
    }
}
