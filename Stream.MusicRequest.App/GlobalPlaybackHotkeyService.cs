using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace Stream.MusicRequest.App;

public enum PlaybackHotkeyAction
{
    NextTrack,
    PreviousTrack,
    PlayPause
}

public sealed class PlaybackHotkeyBinding
{
    public PlaybackHotkeyAction Action { get; set; }
    public uint VirtualKey { get; set; }
    public uint Modifiers { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class PlaybackHotkeySettings
{
    public List<PlaybackHotkeyBinding> Bindings { get; set; } = [];
}

public sealed class PlaybackHotkeySettingsStorage
{
    private readonly string _path;

    public PlaybackHotkeySettingsStorage(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        Directory.CreateDirectory(settingsDirectory);
        _path = Path.Combine(settingsDirectory, "hotkeys.json");
    }

    public PlaybackHotkeySettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new PlaybackHotkeySettings();

            return JsonSerializer.Deserialize<PlaybackHotkeySettings>(
                File.ReadAllText(_path)) ?? new PlaybackHotkeySettings();
        }
        catch
        {
            // A damaged hotkey file must not prevent the player from starting.
            return new PlaybackHotkeySettings();
        }
    }

    public async Task SaveAsync(PlaybackHotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(_path, json);
    }
}

/// <summary>
/// Registers system-wide playback shortcuts without installing a low-level
/// keyboard hook or intercepting ordinary keyboard input.
/// </summary>
public sealed class GlobalPlaybackHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int FirstHotkeyId = 0x5100;

    private readonly Window _window;
    private readonly Action<PlaybackHotkeyAction> _onHotkey;
    private readonly Dictionary<int, PlaybackHotkeyBinding> _registered = [];
    private HwndSource? _source;
    private bool _disposed;

    public GlobalPlaybackHotkeyService(
        Window window,
        Action<PlaybackHotkeyAction> onHotkey)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _onHotkey = onHotkey ?? throw new ArgumentNullException(nameof(onHotkey));
        _window.SourceInitialized += Window_SourceInitialized;
        _window.Closed += Window_Closed;
    }

    public bool TryApply(
        IReadOnlyCollection<PlaybackHotkeyBinding> bindings,
        out string? error)
    {
        error = null;
        if (_source == null)
        {
            error = "Окно плеера ещё не инициализировано.";
            return false;
        }

        var normalized = bindings
            .Where(binding => binding.VirtualKey != 0)
            .ToList();

        if (normalized.Any(binding => binding.Modifiers == 0))
        {
            error = "Для глобальной горячей клавиши нужен хотя бы один модификатор: Ctrl, Alt, Shift или Win.";
            return false;
        }

        var duplicate = normalized
            .GroupBy(binding => (binding.Modifiers, binding.VirtualKey))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            error = "Одна и та же комбинация назначена нескольким действиям.";
            return false;
        }

        // Reuse registrations whose physical key combination is unchanged.
        // New combinations are registered first, so a conflict leaves the old
        // working configuration intact.
        var proposed = new Dictionary<int, PlaybackHotkeyBinding>();
        var newlyRegisteredIds = new List<int>();
        var nextId = FirstHotkeyId;

        foreach (var binding in normalized)
        {
            var existing = _registered.FirstOrDefault(pair =>
                pair.Value.Modifiers == binding.Modifiers &&
                pair.Value.VirtualKey == binding.VirtualKey);

            if (existing.Value != null)
            {
                proposed[existing.Key] = binding;
                continue;
            }

            while (_registered.ContainsKey(nextId) || proposed.ContainsKey(nextId))
                nextId++;

            if (!RegisterHotKey(_source.Handle, nextId,
                    binding.Modifiers | ModNoRepeat, binding.VirtualKey))
            {
                foreach (var id in newlyRegisteredIds)
                    UnregisterHotKey(_source.Handle, id);

                error = $"Не удалось зарегистрировать сочетание «{binding.DisplayName}». Возможно, оно уже занято другой программой.";
                return false;
            }

            newlyRegisteredIds.Add(nextId);
            proposed[nextId] = binding;
            nextId++;
        }

        foreach (var id in _registered.Keys.Where(id => !proposed.ContainsKey(id)).ToList())
            UnregisterHotKey(_source.Handle, id);

        _registered.Clear();
        foreach (var pair in proposed)
            _registered[pair.Key] = pair.Value;
        return true;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(_window);
        _source?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey)
            return IntPtr.Zero;

        var id = wParam.ToInt32();
        if (_registered.TryGetValue(id, out var binding))
        {
            handled = true;
            try
            {
                _onHotkey(binding.Action);
            }
            catch (Exception ex)
            {
                Core.Diagnostics.FileLogger.Error("Ошибка выполнения глобальной горячей клавиши.", ex);
            }
        }

        return IntPtr.Zero;
    }

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _window.SourceInitialized -= Window_SourceInitialized;
        _window.Closed -= Window_Closed;

        if (_source != null)
        {
            foreach (var id in _registered.Keys)
                UnregisterHotKey(_source.Handle, id);

            _registered.Clear();
            _source.RemoveHook(WindowProc);
            _source = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
