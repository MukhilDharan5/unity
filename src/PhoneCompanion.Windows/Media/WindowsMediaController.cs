using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhoneCompanion.Core.Models;
using Windows.Media.Control;

namespace PhoneCompanion.Windows.Media;

public sealed class WindowsMediaController : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaState? _current;
    private bool _disposed;

    public MediaState? Current { get { lock (_sync) return _current; } }
    public event Action<MediaState?>? StateChanged;

    public static async Task<WindowsMediaController> CreateAsync()
    {
        var controller = new WindowsMediaController();
        try
        {
            controller._manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            controller._manager.CurrentSessionChanged += controller.OnCurrentSessionChanged;
            controller.BindCurrentSession();
            await controller.RefreshAsync();
        }
        catch { controller.Publish(null); }
        return controller;
    }

    public async Task ExecuteAsync(MediaCommand command)
    {
        var session = _session;
        if (_disposed || session is null) return;
        try
        {
            var controls = session.GetPlaybackInfo().Controls;
            _ = command switch
            {
                MediaCommand.PlayPause when controls.IsPlayPauseToggleEnabled => await session.TryTogglePlayPauseAsync(),
                MediaCommand.NextTrack when controls.IsNextEnabled => await session.TrySkipNextAsync(),
                MediaCommand.PreviousTrack when controls.IsPreviousEnabled => await session.TrySkipPreviousAsync(),
                _ => false
            };
        }
        catch { }
        await RefreshAsync();
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        BindCurrentSession();
        _ = RefreshAsync();
    }

    private void BindCurrentSession()
    {
        var next = _manager?.GetCurrentSession();
        if (ReferenceEquals(next, _session)) return;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
        _session = next;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_disposed || !await _refresh.WaitAsync(0)) return;
        try
        {
            var session = _session;
            if (session is null) { Publish(null); return; }
            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var controls = playback.Controls;
            Publish(new MediaState(
                Normalize(FriendlySource(session.SourceAppUserModelId), 80),
                Normalize(properties.Title, 256),
                Normalize(properties.Artist, 256),
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                new MediaCapabilities(controls.IsPlayPauseToggleEnabled, controls.IsNextEnabled,
                    controls.IsPreviousEnabled)));
        }
        catch { Publish(null); }
        finally { _refresh.Release(); }
    }

    private void Publish(MediaState? state)
    {
        lock (_sync)
        {
            if (_current == state) return;
            _current = state;
        }
        StateChanged?.Invoke(state);
    }

    private static string? FriendlySource(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;
        var bang = appId.LastIndexOf('!');
        var value = bang >= 0 && bang < appId.Length - 1 ? appId[(bang + 1)..] : appId;
        return Path.GetFileNameWithoutExtension(value);
    }

    private static string? Normalize(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = string.Concat(value.Trim().Select(character => char.IsControl(character) ? ' ' : character)).Trim();
        if (cleaned.Length == 0) return null;
        if (cleaned.Length <= max) return cleaned;
        var end = max;
        if (end > 0 && char.IsHighSurrogate(cleaned[end - 1])) end--;
        return cleaned[..end];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
        _session = null;
        _manager = null;
    }
}
