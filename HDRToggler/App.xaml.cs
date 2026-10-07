using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace HDRToggler;

public partial class App : System.Windows.Application
{
    private NotifyIcon _trayIcon = null!;
    private System.Drawing.Icon? _appIcon;
    private MainWindow? _mainWindow;
    private AutoHdrService? _autoHdrService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _autoHdrService = new AutoHdrService();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(
                $"Automatic HDR rules could not be loaded:\n\n{ex.Message}",
                "HDR Toggler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _autoHdrService = new AutoHdrService(loadRules: false);
        }

        _appIcon = LoadEmbeddedIcon();

        _trayIcon = new NotifyIcon
        {
            Icon    = _appIcon ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text    = "HDR Toggler",
        };

        _autoHdrService!.ErrorOccurred += OnAutoHdrError;
        _trayIcon.MouseClick += TrayIcon_MouseClick;

        OpenMainWindow();
    }

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            OpenMainWindow();
        }
        else if (e.Button == MouseButtons.Right)
        {
            var menu = new TrayMenuWindow(OpenMainWindow, DoExit);
            menu.ShowNearCursor();
        }
    }

    private void OpenMainWindow()
    {
        if (_mainWindow is { IsVisible: true })
        {
            _mainWindow.Activate();
            return;
        }

        _mainWindow = new MainWindow(HdrService.GetMonitors(), _autoHdrService!);
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void DoExit()
    {
        _autoHdrService?.Dispose();
        if (_autoHdrService?.ShutdownError is { } restoreError)
        {
            MessageBox.Show(
                restoreError,
                "HDR Toggler",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // Hide tray icon immediately for instant visual feedback
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon?.Dispose();
        Shutdown();
    }

    private void OnAutoHdrError(string message)
        => _trayIcon.ShowBalloonTip(5000, "Automatic HDR", message, ToolTipIcon.Warning);

    private static System.Drawing.Icon? LoadEmbeddedIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("HDRToggler.icon.HDRToggler.ico");
        if (stream is null) return null;
        return new System.Drawing.Icon(stream);
    }

}
