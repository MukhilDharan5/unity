using System;
using Windows.Networking.Connectivity;

namespace PhoneCompanion.Windows.Connection;

public sealed class InternetConnectivityMonitor : IDisposable
{
    private volatile bool _hasInternet;

    public InternetConnectivityMonitor()
    {
        _hasInternet = ReadInternetAccess();
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
    }

    public bool HasInternet => _hasInternet;
    public event Action? Changed;

    private void OnNetworkStatusChanged(object sender)
    {
        var next = ReadInternetAccess();
        if (next == _hasInternet) return;
        _hasInternet = next;
        Changed?.Invoke();
    }

    private static bool ReadInternetAccess()
    {
        try
        {
            return NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() ==
                NetworkConnectivityLevel.InternetAccess;
        }
        catch { return false; }
    }

    public void Dispose() => NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
}
