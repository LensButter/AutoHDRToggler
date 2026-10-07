using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace HDRToggler;

public sealed record RunningProcessOption(string ProcessName, int ProcessId, string WindowTitle)
{
    public string DisplayName => string.IsNullOrWhiteSpace(WindowTitle)
        ? $"{ProcessName}.exe (PID {ProcessId})"
        : $"{ProcessName}.exe — {WindowTitle} (PID {ProcessId})";
}

public partial class MainWindow : Window
{
    private List<HdrMonitor> _monitors;
    private readonly AutoHdrService _autoHdrService;
    private bool _updatingAutoHdrCheckbox;

    public MainWindow(IEnumerable<HdrMonitor> monitors, AutoHdrService autoHdrService)
    {
        InitializeComponent();
        _autoHdrService = autoHdrService;
        _monitors = monitors.ToList();
        MonitorList.ItemsSource = _monitors;
        AutoHdrMonitorBox.ItemsSource = _monitors;
        if (_monitors.Count > 0)
            AutoHdrMonitorBox.SelectedIndex = 0;
        AutoHdrEnabledBox.IsChecked = _autoHdrService.IsEnabled;
        RefreshAutoHdrRules();
        RefreshRunningProcesses();

        HdrService.StateChanged += OnExternalStateChanged;
        Closed += (_, _) => HdrService.StateChanged -= OnExternalStateChanged;
    }

    // Called when another window triggers a toggle — re-query from Windows
    private void OnExternalStateChanged(object? source)
    {
        if (source == this) return;
        Dispatcher.Invoke(() =>
        {
            _monitors = HdrService.GetMonitors();
            MonitorList.ItemsSource = _monitors;
            AutoHdrMonitorBox.ItemsSource = _monitors;
            if (AutoHdrMonitorBox.SelectedIndex < 0 && _monitors.Count > 0)
                AutoHdrMonitorBox.SelectedIndex = 0;
        });
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.Source == Root && e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void MonitorBox_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (((FrameworkElement)sender).DataContext is not HdrMonitor monitor) return;

        // Optimistic update — flip immediately without waiting for Windows
        _monitors = _monitors
            .Select(m => m.TargetId == monitor.TargetId ? m with { HdrEnabled = !m.HdrEnabled } : m)
            .ToList();
        MonitorList.ItemsSource = _monitors;

        HdrService.ToggleHdr(monitor, source: this); // notifies other windows
    }

    private void AutoHdrEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingAutoHdrCheckbox) return;

        try
        {
            _autoHdrService.SetEnabled(AutoHdrEnabledBox.IsChecked == true);
        }
        catch (IOException ex)
        {
            ShowAutoHdrSettingsError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowAutoHdrSettingsError(ex.Message);
        }
    }

    private void AddAutoHdrRule_Click(object sender, RoutedEventArgs e)
    {
        if (AutoHdrMonitorBox.SelectedItem is not HdrMonitor monitor)
        {
            MessageBox.Show(this, "No HDR-capable monitors are currently available.",
                "Automatic HDR", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _autoHdrService.AddRule(ProcessNameBox.Text, monitor);
            ProcessNameBox.Clear();
            RefreshAutoHdrRules();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid process name",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Automatic HDR",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (IOException ex)
        {
            ShowAutoHdrSettingsError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowAutoHdrSettingsError(ex.Message);
        }
    }

    private void BrowseProcess_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select application",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true) return;

        ProcessNameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private void RefreshProcesses_Click(object sender, RoutedEventArgs e)
        => RefreshRunningProcesses();

    private void RunningProcess_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RunningProcessBox.SelectedItem is RunningProcessOption process)
            ProcessNameBox.Text = process.ProcessName;
    }

    private void RefreshRunningProcesses()
    {
        var selectedProcess = RunningProcessBox.SelectedItem as RunningProcessOption;
        var processes = new List<RunningProcessOption>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    processes.Add(new RunningProcessOption(
                        process.ProcessName,
                        process.Id,
                        process.MainWindowTitle));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the list was being refreshed.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Some protected processes do not expose window metadata.
                }
            }
        }

        RunningProcessBox.ItemsSource = processes
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.ProcessId)
            .ToList();

        if (selectedProcess is not null)
        {
            RunningProcessBox.SelectedItem = processes.FirstOrDefault(process =>
                process.ProcessId == selectedProcess.ProcessId);
        }
    }

    private void RemoveAutoHdrRule_Click(object sender, RoutedEventArgs e)
    {
        if (AutoHdrRuleList.SelectedItem is not AutoHdrRule rule) return;

        try
        {
            _autoHdrService.RemoveRule(rule);
            RefreshAutoHdrRules();
        }
        catch (IOException ex)
        {
            ShowAutoHdrSettingsError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowAutoHdrSettingsError(ex.Message);
        }
    }

    private void RefreshAutoHdrRules()
        => AutoHdrRuleList.ItemsSource = _autoHdrService.Rules.ToList();

    private void ShowAutoHdrSettingsError(string detail)
    {
        _updatingAutoHdrCheckbox = true;
        AutoHdrEnabledBox.IsChecked = _autoHdrService.IsEnabled;
        _updatingAutoHdrCheckbox = false;
        MessageBox.Show(this, $"Automatic HDR settings could not be saved:\n\n{detail}",
            "HDR Toggler", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
