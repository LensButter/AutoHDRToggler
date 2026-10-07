using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace HDRToggler;

public partial class TrayMenuWindow : Window
{
    private readonly AutoHdrService _autoHdrService;
    private readonly Action _onOpenSettings;
    private readonly Action _onExit;
    private List<HdrMonitor> _monitors;
    private bool _closing;

    public TrayMenuWindow(AutoHdrService autoHdrService, Action onOpenSettings, Action onExit)
    {
        InitializeComponent();
        _autoHdrService = autoHdrService;
        _onOpenSettings = onOpenSettings;
        _onExit = onExit;
        UpdateAutoHdrStatus(_autoHdrService.IsEnabled);
        _monitors = HdrService.GetMonitors();
        MonitorList.ItemsSource = _monitors;

        _autoHdrService.EnabledChanged += OnAutoHdrEnabledChanged;
        HdrService.StateChanged += OnExternalStateChanged;
        Closed += (_, _) =>
        {
            HdrService.StateChanged -= OnExternalStateChanged;
            _autoHdrService.EnabledChanged -= OnAutoHdrEnabledChanged;
        };
    }

    // Called when another window triggers a toggle — re-query from Windows
    private void OnExternalStateChanged(object? source)
    {
        if (source == this) return;
        Dispatcher.Invoke(() =>
        {
            _monitors = HdrService.GetMonitors();
            MonitorList.ItemsSource = _monitors;
        });
    }

    /// <summary>
    /// Snapshot cursor position now; actual repositioning happens in Loaded
    /// once the window has a PresentationSource and a real rendered size.
    /// </summary>
    public void ShowNearCursor()
    {
        _cursorSnapshot = System.Windows.Forms.Cursor.Position;

        // Start off-screen so nothing flickers
        Left = -10000;
        Top  = -10000;
        Show();
        Activate();
    }

    private System.Drawing.Point _cursorSnapshot;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;

        // TransformFromDevice converts physical device pixels → WPF logical units
        var transform = source.CompositionTarget.TransformFromDevice;

        var cursor = _cursorSnapshot;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var work   = screen.WorkingArea; // physical pixels, excludes taskbar

        var curLogical  = transform.Transform(new System.Windows.Point(cursor.X, cursor.Y));
        var workTopLeft = transform.Transform(new System.Windows.Point(work.Left, work.Top));
        var workBotRight = transform.Transform(new System.Windows.Point(work.Right, work.Bottom));

        double w = ActualWidth;
        double h = ActualHeight;

        // Prefer placing above & left of cursor
        double x = curLogical.X - w;
        double y = curLogical.Y - h - 8;

        Left = Math.Max(workTopLeft.X, Math.Min(x, workBotRight.X - w));
        Top  = Math.Max(workTopLeft.Y, Math.Min(y, workBotRight.Y - h));
    }

    private void MonitorItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not HdrMonitor monitor) return;

        // Optimistic update
        _monitors = _monitors
            .Select(m => m.TargetId == monitor.TargetId ? m with { HdrEnabled = !m.HdrEnabled } : m)
            .ToList();
        MonitorList.ItemsSource = _monitors;

        HdrService.ToggleHdr(monitor, source: this);
    }

    private void ExitItem_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_closing) return;
        _closing = true;
        Close();
        _onExit();
    }

    private void AutoHdrItem_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_closing) return;

        try
        {
            _autoHdrService.SetEnabled(!_autoHdrService.IsEnabled);
            UpdateAutoHdrStatus(_autoHdrService.IsEnabled);
        }
        catch (IOException ex)
        {
            ShowAutoHdrToggleError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowAutoHdrToggleError(ex.Message);
        }
    }

    private void SettingsItem_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_closing) return;
        _closing = true;
        Close();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, _onOpenSettings);
    }

    private void ShowAutoHdrToggleError(string detail)
    {
        UpdateAutoHdrStatus(_autoHdrService.IsEnabled);
        MessageBox.Show(this, $"Automatic HDR settings could not be saved:\n\n{detail}",
            "HDR Toggler", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnAutoHdrEnabledChanged(bool enabled)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnAutoHdrEnabledChanged(enabled));
            return;
        }

        UpdateAutoHdrStatus(enabled);
    }

    private void UpdateAutoHdrStatus(bool enabled)
    {
        AutoHdrStatusText.Text = enabled ? "ON" : "OFF";
        AutoHdrStatusText.Foreground = enabled
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x87))
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
        AutoHdrStatusBadge.Background = enabled
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x26, 0x00, 0xFF, 0x87))
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            Close();
        }
    }
}
