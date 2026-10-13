using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PrintTool.Ui.Shared.Interop;

/// <summary>
/// Aplica o efeito Mica do Windows 11 (fundo translúcido nativo) numa janela, e segue o
/// tema claro/escuro do sistema. Em versões do Windows sem suporte (10, ou 11 anterior ao
/// 22H2), as chamadas simplesmente falham e a janela fica com o fundo sólido normal — por
/// isso cada P/Invoke é blindado com try/catch, nunca interrompe a abertura da janela.
/// </summary>
public static class WindowBackdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2; // Mica

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    /// <summary>
    /// Chamar no <c>SourceInitialized</c> da janela (depois que o handle do Win32 já existe).
    /// </summary>
    public static void Apply(Window window, bool useDarkMode)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int darkMode = useDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            int backdropType = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }
        catch
        {
            // Windows mais antigo, ou qualquer outra falha de interop — segue com o fundo sólido padrão.
        }
    }
}
