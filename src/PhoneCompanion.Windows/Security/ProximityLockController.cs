using System;
using System.Threading;
using System.Threading.Tasks;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.State;

namespace PhoneCompanion.Windows.Security;

public sealed class ProximityLockController : IDisposable
{
    private static readonly TimeSpan AbsenceGrace = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequiredIdle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ResumeGap = TimeSpan.FromSeconds(20);

    private readonly object _sync = new();
    private readonly PhoneStateManager _manager;
    private CancellationTokenSource? _monitor;
    private bool _enabled;
    private bool _armed;
    private bool _lockedThisAbsence;
    private bool _disposed;
    private PhoneState _phoneState;
    private string _status = "Off for this app session";

    public ProximityLockController(PhoneStateManager manager)
    {
        _manager = manager;
        _phoneState = manager.Current;
        manager.StateChanged += OnPhoneStateChanged;
    }

    public event Action? Changed;
    public bool Enabled { get { lock (_sync) return _enabled; } }
    public string Status { get { lock (_sync) return _status; } }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_disposed || _enabled == enabled) return;
            _enabled = enabled;
            CancelMonitorLocked();
            _lockedThisAbsence = false;
            if (!enabled)
            {
                _armed = false;
                _status = "Off for this app session";
            }
            else if (IsTrustedConnected(_phoneState))
            {
                _armed = true;
                _status = "Monitoring your trusted phone";
            }
            else
            {
                _armed = false;
                _status = "Waiting for a trusted phone connection";
            }
        }
        Changed?.Invoke();
    }

    private void OnPhoneStateChanged(PhoneState state)
    {
        var changed = false;
        lock (_sync)
        {
            if (_disposed) return;
            _phoneState = state;
            if (!_enabled) return;

            if (state.IsDemo)
            {
                _armed = false;
                _lockedThisAbsence = false;
                CancelMonitorLocked();
                changed = SetStatusLocked("Waiting for a trusted phone connection");
            }
            else if (IsTrustedConnected(state))
            {
                _armed = true;
                _lockedThisAbsence = false;
                CancelMonitorLocked();
                changed = SetStatusLocked("Monitoring your trusted phone");
            }
            else if (_armed && !_lockedThisAbsence && _monitor is null)
            {
                StartMonitorLocked();
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    private void StartMonitorLocked()
    {
        var monitor = new CancellationTokenSource();
        _monitor = monitor;
        _status = "Phone absent · reconnect grace: 2 minutes";
        _ = MonitorAbsenceAsync(monitor);
    }

    private async Task MonitorAbsenceAsync(CancellationTokenSource monitor)
    {
        var token = monitor.Token;
        try
        {
            var absentSince = DateTimeOffset.UtcNow;
            var previousTick = absentSince;
            while (true)
            {
                await Task.Delay(PollInterval, token).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                if (now - previousTick > ResumeGap)
                {
                    absentSince = now;
                    UpdateStatus("Phone absent · reconnect grace restarted after resume", monitor);
                }
                previousTick = now;
                if (now - absentSince < AbsenceGrace) continue;

                if (WindowsWorkstationLock.GetIdleTime() < RequiredIdle)
                {
                    UpdateStatus("Phone absent · waiting for Windows to be idle", monitor);
                    continue;
                }

                lock (_sync)
                {
                    if (token.IsCancellationRequested || _disposed || !_enabled ||
                        !ReferenceEquals(_monitor, monitor) || IsTrustedConnected(_phoneState)) return;
                    _lockedThisAbsence = true;
                    var locked = WindowsWorkstationLock.TryLock();
                    _status = locked ? "Windows locked after phone absence" : "Windows could not be locked";
                    monitor.Dispose();
                    _monitor = null;
                }
                Changed?.Invoke();
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateStatus(string status, CancellationTokenSource monitor)
    {
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(_monitor, monitor) || _status == status) return;
            _status = status;
        }
        Changed?.Invoke();
    }

    private static bool IsTrustedConnected(PhoneState state) =>
        state.Connection == ConnectionState.Connected && state.Transport is not null and not TransportKind.Mock;

    private bool SetStatusLocked(string status)
    {
        if (_status == status) return false;
        _status = status;
        return true;
    }

    private void CancelMonitorLocked()
    {
        _monitor?.Cancel();
        _monitor?.Dispose();
        _monitor = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CancelMonitorLocked();
        }
        _manager.StateChanged -= OnPhoneStateChanged;
    }
}
