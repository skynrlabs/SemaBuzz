using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SemaBuzz.App;

public partial class SendFileDialog : Window
{
    private readonly string _filePath;

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".msi", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".scr", ".pif", ".com", ".jar", ".reg", ".inf", ".lnk",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".cab", ".iso",
    };

    public string FileName  { get; private set; } = string.Empty;
    public byte[] FileBytes { get; private set; } = [];

    public SendFileDialog(string filePath)
    {
        InitializeComponent();
        _filePath        = filePath;
        var fi           = new FileInfo(filePath);
        FileNameText.Text = fi.Name;
        FileSizeText.Text = FormatSize(fi.Length);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SemaBuzzThemeManager.ApplyChrome(this);
        SemaBuzzTheme.HideCloseButton(this);
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        ClearError();

        var ext = Path.GetExtension(_filePath);
        if (BlockedExtensions.Contains(ext))
        {
            ShowError($"Cannot send {ext} files — this file type is not allowed.");
            return;
        }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(_filePath); }
        catch (Exception ex)
        {
            ShowError($"Could not read file: {ex.Message}");
            return;
        }

        const long maxBytes = 10L * 1024 * 1024;
        if (bytes.Length > maxBytes)
        {
            ShowError($"File is too large ({bytes.Length / 1024.0 / 1024.0:F1} MB). Maximum is 10 MB.");
            return;
        }

        if (HasDangerousMagicBytes(bytes))
        {
            ShowError("This file appears to be an executable or archive — it cannot be sent.");
            return;
        }

        FileName     = Path.GetFileName(_filePath);
        FileBytes    = bytes;
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        FileErrorText.Text       = message;
        FileErrorText.Visibility = Visibility.Visible;
        OuterBorder.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
    }

    private void ClearError()
    {
        FileErrorText.Visibility = Visibility.Collapsed;
        OuterBorder.ClearValue(System.Windows.Controls.Border.BorderBrushProperty);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024        => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _             => $"{bytes / 1024.0 / 1024.0:F1} MB",
    };

    private static bool HasDangerousMagicBytes(byte[] data)
    {
        if (data.Length < 2) return false;
        if (data[0] == 0x4D && data[1] == 0x5A) return true;             // Windows PE (MZ)
        if (data[0] == 0x23 && data[1] == 0x21) return true;             // shebang (#!)
        if (data[0] == 0x1F && data[1] == 0x8B) return true;             // gzip
        if (data.Length < 4) return false;
        if (data[0] == 0x7F && data[1] == 0x45 && data[2] == 0x4C && data[3] == 0x46) return true; // ELF
        if (data[0] == 0xCA && data[1] == 0xFE && data[2] == 0xBA && data[3] == 0xBE) return true; // Mach-O fat
        if (data[0] == 0xCE && data[1] == 0xFA && data[2] == 0xED && data[3] == 0xFE) return true; // Mach-O 32
        if (data[0] == 0xCF && data[1] == 0xFA && data[2] == 0xED && data[3] == 0xFE) return true; // Mach-O 64
        if (data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04) return true; // PK zip/jar
        if (data[0] == 0x52 && data[1] == 0x61 && data[2] == 0x72 && data[3] == 0x21) return true; // RAR
        if (data[0] == 0x42 && data[1] == 0x5A && data[2] == 0x68) return true;                    // bzip2
        if (data[0] == 0x37 && data[1] == 0x7A && data[2] == 0xBC && data[3] == 0xAF) return true; // 7z
        if (data[0] == 0xFD && data[1] == 0x37 && data[2] == 0x7A && data[3] == 0x58) return true; // xz
        return false;
    }
}
