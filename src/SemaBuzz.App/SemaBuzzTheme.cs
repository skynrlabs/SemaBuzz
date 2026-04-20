using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SemaBuzz.App;

/// <summary>
/// Applies the SemaBuzz dark chrome to any window via the Desktop Window Manager API.
///
/// Works on Windows 10 (20H1+) and Windows 11:
///   - DWMWA_USE_IMMERSIVE_DARK_MODE  â†’ dark title bar text/buttons
///   - DWMWA_CAPTION_COLOR            â†’ obsidian caption background  (Win11 22000+)
///   - DWMWA_TEXT_COLOR               â†’ amber caption text           (Win11 22000+)
///   - DWMWA_BORDER_COLOR             â†’ obsidian window border       (Win11 22000+)
///
/// DWM silently ignores unknown attributes on older builds, so it is safe to
/// call unconditionally.
/// </summary>
internal static class SemaBuzzTheme
{
    // â”€â”€â”€ DWM attribute IDs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR            = 34;
    private const int DWMWA_CAPTION_COLOR           = 35;
    private const int DWMWA_TEXT_COLOR              = 36;

    // Special sentinel: DWMWA_COLOR_NONE â€” tells DWM to use the system default
    private const int DWMWA_COLOR_NONE    = unchecked((int)0xFFFFFFFE);
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);

    // â”€â”€â”€ Palette (COLORREF = 0x00BBGGRR) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // #121212 obsidian
    private const int ColorObsidian  = 0x00121212;
    // #FFB300 amber  â†’ R=0xFF G=0xB3 B=0x00 â†’ COLORREF = 0x0000B3FF
    private const int ColorAmber     = 0x0000B3FF;
    // #2A2A2A border â†’ same in all channels
    private const int ColorBorder    = 0x002A2A2A;

    // â”€â”€â”€ P/Invoke â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    private const uint MF_BYCOMMAND = 0x00000000u;
    private const uint SC_CLOSE     = 0xF060u;

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Call this from a window's <c>OnSourceInitialized</c> override so the
    /// HWND is available.  Uses the default Obsidian/Amber palette.
    /// </summary>
    public static void Apply(Window window)
        => Apply(window, ColorObsidian, ColorAmber, ColorBorder);

    /// <summary>
    /// Apply explicit COLORREF palette â€” called by <see cref="SemaBuzzThemeManager"/>
    /// when the user switches themes at runtime.
    /// </summary>
    /// <param name="captionColor">COLORREF (0x00BBGGRR) for the caption bar background.</param>
    /// <param name="textColor">COLORREF for caption text.</param>
    /// <param name="borderColor">COLORREF for the window border.</param>
    public static void Apply(Window window, int captionColor, int textColor, int borderColor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Set(hwnd, DWMWA_CAPTION_COLOR,           captionColor);
        Set(hwnd, DWMWA_TEXT_COLOR,              textColor);
        Set(hwnd, DWMWA_BORDER_COLOR,            borderColor);
    }

    private static void Set(IntPtr hwnd, int attr, int value)
    {
        try { DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)); }
        catch { /* dwmapi unavailable â€” ignore */ }
    }

    /// <summary>
    /// Removes the close (âœ•) button from a dialog window's title bar.
    /// Call from <c>OnSourceInitialized</c> so the HWND is available.
    /// </summary>
    public static void HideCloseButton(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var menu = GetSystemMenu(hwnd, false);
            if (menu != IntPtr.Zero)
                DeleteMenu(menu, SC_CLOSE, MF_BYCOMMAND);
        }
        catch { }
    }
}
