using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace HDRToggler;

public sealed record AutoHdrRule(
    string ProcessName,
    uint AdapterLowPart,
    int AdapterHighPart,
    uint TargetId,
    string MonitorName)
{
    public string DisplayName => $"{ProcessName}.exe | {MonitorName}";
}

public sealed class AutoHdrService : IDisposable
{
    private readonly record struct MonitorKey(uint AdapterLowPart, int AdapterHighPart, uint TargetId);

    private readonly string _rulesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HDRToggler",
        "auto-hdr-rules.json");
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HDRToggler",
        "auto-hdr-enabled.json");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<MonitorKey, bool> _originalStates = [];
    private readonly HashSet<MonitorKey> _reportedFailures = [];
    private List<AutoHdrRule> _rules;
    private bool _enabled;
    private bool _disposed;

    public IReadOnlyList<AutoHdrRule> Rules => _rules;
    public bool IsEnabled => _enabled;
    public event Action<string>? ErrorOccurred;
    public string? ShutdownError { get; private set; }

    public AutoHdrService(bool loadRules = true)
    {
        _rules = loadRules ? LoadRules() : [];
        _enabled = loadRules ? LoadEnabledSetting() : true;
        _timer.Tick += (_, _) => UpdateRules();
        _timer.Start();
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;

        SaveEnabledSetting(enabled);
        _enabled = enabled;
        UpdateRules();
    }

    public void AddRule(string processName, HdrMonitor monitor)
    {
        var normalizedName = processName.Trim();
        if (normalizedName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            normalizedName = normalizedName[..^4];

        if (string.IsNullOrWhiteSpace(normalizedName) ||
            normalizedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalizedName.Contains(Path.DirectorySeparatorChar) ||
            normalizedName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Enter a valid executable name, such as game.exe.", nameof(processName));
        }

        var rule = new AutoHdrRule(
            normalizedName,
            monitor.AdapterId.LowPart,
            monitor.AdapterId.HighPart,
            monitor.TargetId,
            monitor.FriendlyName);

        if (_rules.Any(existing =>
                string.Equals(existing.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                ToKey(existing) == ToKey(rule)))
        {
            throw new InvalidOperationException("That process and monitor rule already exists.");
        }

        var updatedRules = new List<AutoHdrRule>(_rules) { rule };
        SaveRules(updatedRules);
        _rules = updatedRules;
        UpdateRules();
    }

    public void RemoveRule(AutoHdrRule rule)
    {
        if (!_rules.Contains(rule)) return;
        var updatedRules = _rules.Where(existing => existing != rule).ToList();
        SaveRules(updatedRules);
        _rules = updatedRules;
        UpdateRules();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        RestoreAll();
    }

    private List<AutoHdrRule> LoadRules()
    {
        if (!File.Exists(_rulesPath)) return [];

        var json = File.ReadAllText(_rulesPath);
        var rules = JsonSerializer.Deserialize<List<AutoHdrRule>>(json)
            ?? throw new InvalidDataException("The automatic HDR rules file is empty or invalid.");

        if (rules.Any(rule => rule is null ||
                              string.IsNullOrWhiteSpace(rule.ProcessName) ||
                              rule.ProcessName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                              string.IsNullOrWhiteSpace(rule.MonitorName)))
        {
            throw new InvalidDataException("The automatic HDR rules file contains an invalid rule.");
        }

        return rules;
    }

    private bool LoadEnabledSetting()
    {
        if (!File.Exists(_settingsPath)) return true;

        var json = File.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<bool>(json);
    }

    private void SaveEnabledSetting(bool enabled)
    {
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Could not determine the settings directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(enabled));
    }

    private void SaveRules(List<AutoHdrRule> rules)
    {
        var directory = Path.GetDirectoryName(_rulesPath)
            ?? throw new InvalidOperationException("Could not determine the rules directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_rulesPath, JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void UpdateRules()
    {
        if (_disposed) return;

        var activeMonitors = new HashSet<MonitorKey>();
        if (_enabled)
        {
            foreach (var rule in _rules)
            {
                if (IsProcessRunning(rule.ProcessName))
                    activeMonitors.Add(ToKey(rule));
            }
        }

        var monitors = HdrService.GetMonitors();
        foreach (var monitor in monitors)
        {
            var key = ToKey(monitor);
            if (activeMonitors.Contains(key))
            {
                if (!_originalStates.ContainsKey(key))
                    _originalStates[key] = monitor.HdrEnabled;

                if (!monitor.HdrEnabled)
                    HdrService.SetHdr(monitor, enabled: true, source: this);
            }
            else if (_originalStates.TryGetValue(key, out var originalState))
            {
                if (monitor.HdrEnabled == originalState)
                {
                    _reportedFailures.Remove(key);
                    _originalStates.Remove(key);
                }
                else if (TrySetHdr(monitor, originalState, "restore"))
                    _originalStates.Remove(key);
            }
        }

        foreach (var key in _originalStates.Keys
                     .Where(key => !activeMonitors.Contains(key) && monitors.All(monitor => ToKey(monitor) != key))
                     .ToList())
        {
            _originalStates.Remove(key);
            _reportedFailures.Remove(key);
        }

    }

    private void RestoreAll()
    {
        var monitors = HdrService.GetMonitors();
        foreach (var (key, originalState) in _originalStates)
        {
            var monitor = monitors.FirstOrDefault(candidate => ToKey(candidate) == key);
            if (monitor.SupportsHdr && monitor.HdrEnabled != originalState)
            {
                if (!TrySetHdr(monitor, originalState, "restore"))
                    ShutdownError = $"HDR could not be restored on {monitor.FriendlyName}.";
            }
        }

        _originalStates.Clear();
    }

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static MonitorKey ToKey(HdrMonitor monitor)
        => new(monitor.AdapterId.LowPart, monitor.AdapterId.HighPart, monitor.TargetId);

    private static MonitorKey ToKey(AutoHdrRule rule)
        => new(rule.AdapterLowPart, rule.AdapterHighPart, rule.TargetId);

    private bool TrySetHdr(HdrMonitor monitor, bool enabled, string operation)
    {
        var key = ToKey(monitor);
        if (HdrService.SetHdr(monitor, enabled, source: this))
        {
            _reportedFailures.Remove(key);
            return true;
        }

        if (_reportedFailures.Add(key))
            ErrorOccurred?.Invoke($"Windows could not {operation} HDR on {monitor.FriendlyName}.");
        return false;
    }
}
