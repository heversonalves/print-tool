using Microsoft.Win32;

namespace PrintTool.Ui.Shared.Theme;

/// <summary>
/// Lê a preferência de tema claro/escuro do Windows (a mesma chave que o próprio Windows usa
/// para decidir a cor dos apps). Lido uma vez, na abertura da janela — não acompanha mudança
/// de tema em tempo real nesta versão.
/// </summary>
public static class SystemTheme
{
    public static bool IsDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }
}
