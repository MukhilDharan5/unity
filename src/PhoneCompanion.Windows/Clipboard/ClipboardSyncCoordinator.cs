using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Protocol;
using PhoneCompanion.Core.State;

namespace PhoneCompanion.Windows.Clipboard;

public sealed class ClipboardSyncCoordinator : IDisposable
{
    private const int RememberedUpdateLimit = 64;
    private readonly object _sync = new();
    private readonly PhoneStateManager _manager;
    private readonly IWindowsClipboard _clipboard;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Guid> _seenUpdates = [];
    private readonly Queue<Guid> _seenOrder = [];
    private string? _suppressNextText;
    private string? _pendingText;
    private bool _sending;
    private bool _enabled;
    private bool _disposed;

    public ClipboardSyncCoordinator(PhoneStateManager manager, IWindowsClipboard clipboard, Dispatcher dispatcher)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clipboard.TextChanged += OnLocalTextChanged;
        _manager.ClipboardReceived += OnRemoteClipboard;
        _manager.StateChanged += OnPhoneStateChanged;
    }

    public bool Enabled { get { lock (_sync) return _enabled; } }
    public bool CanTransfer => _manager.Current.Connection == ConnectionState.Connected && !_manager.Current.IsDemo;
    public event Action? Changed;
    public event Action<string>? Notice;

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enabled == enabled) return;
            if (enabled)
            {
                // Monitoring begins after explicit opt-in and does not transmit the existing clipboard.
                _clipboard.StartMonitoring();
                _enabled = true;
            }
            else
            {
                _enabled = false;
                _pendingText = null;
                _suppressNextText = null;
                _clipboard.StopMonitoring();
            }
        }
        Changed?.Invoke();
    }

    private void OnPhoneStateChanged(PhoneState state) => Changed?.Invoke();

    private async void OnLocalTextChanged(string text)
    {
        try
        {
            bool startSender;
            lock (_sync)
            {
                if (_disposed || !_enabled) return;
                if (string.Equals(_suppressNextText, text, StringComparison.Ordinal))
                {
                    _suppressNextText = null;
                    return;
                }
                _suppressNextText = null;
                _pendingText = text;
                startSender = !_sending;
                if (startSender) _sending = true;
            }
            if (startSender) await DrainOutboundAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { Notice?.Invoke("Clipboard sync couldn't send this item."); }
    }

    private async Task DrainOutboundAsync()
    {
        while (true)
        {
            string? text;
            lock (_sync)
            {
                if (_disposed || !_enabled || _pendingText is null)
                {
                    _sending = false;
                    return;
                }
                text = _pendingText;
                _pendingText = null;
            }
            if (text.Contains('\0') || Encoding.UTF8.GetByteCount(text) > JsonPhoneMessageCodec.MaxClipboardTextBytes)
            {
                Notice?.Invoke("Clipboard text is too large to sync.");
                continue;
            }
            var update = new ClipboardContent(Guid.NewGuid(), text);
            Remember(update.UpdateId);
            var result = await _manager.SendClipboardAsync(update, _lifetime.Token).ConfigureAwait(false);
            if (result == CommandResult.Failed) Notice?.Invoke("Clipboard sync couldn't reach your phone.");
        }
    }

    private void OnRemoteClipboard(ClipboardContent content)
    {
        lock (_sync)
        {
            if (_disposed || !_enabled || !Remember(content.UpdateId)) return;
        }
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() =>
        {
            lock (_sync)
            {
                if (_disposed || !_enabled) return;
                _suppressNextText = content.Text;
            }
            if (!_clipboard.TrySetText(content.Text))
            {
                lock (_sync) _suppressNextText = null;
                Notice?.Invoke("Clipboard sync couldn't update the Windows clipboard.");
            }
        }, DispatcherPriority.Background);
    }

    private bool Remember(Guid updateId)
    {
        lock (_sync)
        {
            if (!_seenUpdates.Add(updateId)) return false;
            _seenOrder.Enqueue(updateId);
            while (_seenOrder.Count > RememberedUpdateLimit) _seenUpdates.Remove(_seenOrder.Dequeue());
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            _pendingText = null;
            _lifetime.Cancel();
        }
        _clipboard.TextChanged -= OnLocalTextChanged;
        _manager.ClipboardReceived -= OnRemoteClipboard;
        _manager.StateChanged -= OnPhoneStateChanged;
        _clipboard.Dispose();
        _lifetime.Dispose();
    }
}
