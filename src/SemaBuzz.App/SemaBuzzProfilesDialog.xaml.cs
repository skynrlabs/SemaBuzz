using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SemaBuzz.App;

public partial class SemaBuzzProfilesDialog : Window
{
    private List<SemaBuzzProfile> _profiles = [];
    private SemaBuzzProfile?      _editingProfile;
    private byte[]?               _editorAvatarPng;
    private string?               _selectedProfileId;
    private readonly bool         _lockDelete;

    public SemaBuzzProfilesDialog(bool lockDelete = false)
    {
        InitializeComponent();

        _lockDelete        = lockDelete;
        _profiles          = SemaBuzzProfileStore.Load();
        _selectedProfileId = App.Settings.ActiveProfileId
            ?? (_profiles.Count > 0 ? _profiles[0].Id : null);
        RebuildProfileRows();
        if (_lockDelete)
        {
            EditProfileBtn.ToolTip     = "Cannot edit a profile while a Buzz is active.";
            ActiveWireNotice.Visibility = Visibility.Visible;
        }
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

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    // ── Profile management ────────────────────────────────────────────────────

    private void RebuildProfileRows()
    {
        ProfileItemsPanel.Children.Clear();
        var accentBrush = new SolidColorBrush(SemaBuzzThemeManager.AccentColor);
        var dimBrush    = new SolidColorBrush(Color.FromArgb(0x9E, 0x9E, 0x9E, 0x9E));

        foreach (var profile in _profiles)
        {
            var p = profile;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var radio = new RadioButton
            {
                GroupName         = "ProfileSelect",
                IsChecked         = p.Id == _selectedProfileId,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 10, 0),
                Foreground        = accentBrush,
            };
            radio.Checked += (_, _) =>
            {
                _selectedProfileId           = p.Id;
                EditProfileBtn.IsEnabled     = !_lockDelete;
                App.Settings.ActiveProfileId = p.Id;
                App.Settings.Save();
            };
            Grid.SetColumn(radio, 0);
            row.Children.Add(radio);

            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width           = 36,
                Height          = 36,
                Stroke          = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                StrokeThickness = 1,
                Margin          = new Thickness(0, 0, 10, 0),
            };
            if (p.AvatarPng is { } png)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new System.IO.MemoryStream(png);
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                ellipse.Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            else
            {
                ellipse.Fill = InitialsBrush(p.Handle, SemaBuzzThemeManager.AccentColor);
            }
            Grid.SetColumn(ellipse, 1);
            row.Children.Add(ellipse);

            var handleText = new TextBlock
            {
                Text              = p.Handle,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 10, 0),
                Foreground        = (System.Windows.Media.Brush)Application.Current.Resources["AmberBrush"],
            };
            Grid.SetColumn(handleText, 2);
            row.Children.Add(handleText);

            var deleteBtn = new Button
            {
                Content   = "DELETE",
                Style     = (Style)FindResource("SemaBuzzButton"),
                Margin    = new Thickness(0),
                IsEnabled = !_lockDelete,
                ToolTip   = _lockDelete ? "Cannot delete a profile while a Buzz is active." : null,
            };
            deleteBtn.Click += (_, _) =>
            {
                _profiles.Remove(p);
                if (_selectedProfileId == p.Id)
                {
                    _selectedProfileId           = _profiles.Count > 0 ? _profiles[0].Id : null;
                    App.Settings.ActiveProfileId = _selectedProfileId;
                    App.Settings.Save();
                }
                SemaBuzzProfileStore.Save(_profiles);
                RebuildProfileRows();
                EditProfileBtn.IsEnabled = _selectedProfileId != null && !_lockDelete;
            };
            Grid.SetColumn(deleteBtn, 4);
            row.Children.Add(deleteBtn);

            ProfileItemsPanel.Children.Add(row);
        }

        if (_profiles.Count == 0)
        {
            ProfileItemsPanel.Children.Add(new TextBlock
            {
                Text       = "No profiles yet — click + ADD to create one.",
                Foreground = dimBrush,
                FontSize   = 11,
                Margin     = new Thickness(0, 0, 0, 4),
            });
        }

        EditProfileBtn.IsEnabled = _selectedProfileId != null && !_lockDelete;
    }

    private static System.Windows.Media.Brush InitialsBrush(string handle, Color accent)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null, new Rect(0, 0, 36, 36));
            var initial = handle.Length > 0 ? handle[0].ToString().ToUpper() : "?";
            var ft = new FormattedText(initial,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Cascadia Code"),
                16,
                new SolidColorBrush(accent),
                VisualTreeHelper.GetDpi(dv).PixelsPerDip);
            dc.DrawText(ft, new System.Windows.Point((36 - ft.Width) / 2, (36 - ft.Height) / 2));
        }
        var rt = new RenderTargetBitmap(36, 36, 96, 96, PixelFormats.Pbgra32);
        rt.Render(dv);
        rt.Freeze();
        return new ImageBrush(rt) { Stretch = Stretch.None };
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        _editingProfile                  = null;
        _editorAvatarPng                 = null;
        ProfileHandleBox.Text            = string.Empty;
        HandleErrorText.Visibility       = Visibility.Collapsed;
        ProfileAvatarPreview.Fill        = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        ProfileClearAvatarBtn.IsEnabled  = false;
        ProfileEditorPanel.Visibility    = Visibility.Visible;
        ProfileHandleBox.Focus();
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        var target = _profiles.FirstOrDefault(p => p.Id == _selectedProfileId);
        if (target is null) return;
        _editingProfile       = target;
        _editorAvatarPng      = target.AvatarPng;
        ProfileHandleBox.Text = target.Handle;
        if (target.AvatarPng is { } png)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new System.IO.MemoryStream(png);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            ProfileAvatarPreview.Fill           = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            ProfileClearAvatarBtn.IsEnabled = true;
        }
        else
        {
            ProfileAvatarPreview.Fill           = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            ProfileClearAvatarBtn.IsEnabled = false;
        }
        HandleErrorText.Visibility    = Visibility.Collapsed;
        ProfileEditorPanel.Visibility = Visibility.Visible;
        ProfileHandleBox.Focus();
    }

    private void ProfileHandleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HandleErrorText != null)
            HandleErrorText.Visibility = Visibility.Collapsed;
    }

    private void ProfileSave_Click(object sender, RoutedEventArgs e)
    {
        var handle = ProfileHandleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            HandleErrorText.Visibility = Visibility.Visible;
            ProfileHandleBox.Focus();
            return;
        }

        if (_editingProfile is not null)
        {
            _editingProfile.Handle       = handle;
            _editingProfile.AvatarBase64 = _editorAvatarPng != null
                ? Convert.ToBase64String(_editorAvatarPng) : null;
        }
        else
        {
            var p = new SemaBuzzProfile
            {
                Handle       = handle,
                AvatarBase64 = _editorAvatarPng != null
                    ? Convert.ToBase64String(_editorAvatarPng) : null,
            };
            _profiles.Add(p);
            _selectedProfileId           = p.Id;
            App.Settings.ActiveProfileId = p.Id;
            App.Settings.Save();
        }

        SemaBuzzProfileStore.Save(_profiles);
        ProfileEditorPanel.Visibility = Visibility.Collapsed;
        _editingProfile  = null;
        _editorAvatarPng = null;
        RebuildProfileRows();
    }

    private void ProfileEditorCancel_Click(object sender, RoutedEventArgs e)
    {
        ProfileEditorPanel.Visibility = Visibility.Collapsed;
        _editingProfile  = null;
        _editorAvatarPng = null;
    }

    private void ProfileChooseAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Choose Avatar Image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource         = new Uri(dlg.FileName);
            src.DecodePixelWidth  = 48;
            src.DecodePixelHeight = 48;
            src.CacheOption       = BitmapCacheOption.OnLoad;
            src.EndInit();
            src.Freeze();
            using var ms = new System.IO.MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            encoder.Save(ms);
            _editorAvatarPng          = ms.ToArray();
            ProfileAvatarPreview.Fill = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
            ProfileClearAvatarBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load image: {ex.Message}", "SemaBuzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ProfileClearAvatar_Click(object sender, RoutedEventArgs e)
    {
        _editorAvatarPng          = null;
        ProfileAvatarPreview.Fill = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        ProfileClearAvatarBtn.IsEnabled = false;
    }

}
