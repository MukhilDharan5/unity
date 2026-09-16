using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Protocol;
using PhoneCompanion.Core.State;
using PhoneCompanion.Core.Transports;
using PhoneCompanion.Windows.Clipboard;
using PhoneCompanion.Windows.ViewModels;

namespace PhoneCompanion.Windows.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-smoke");
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SystemThemeService.ApplyPalette(app.Resources, AppTheme.Light);
        var exitCode = 0;
        app.Startup += async (_, _) =>
        {
            try { await RunAsync(output); }
            catch (Exception e) { Console.Error.WriteLine(e); exitCode = 1; }
            finally { app.Shutdown(exitCode); }
        };
        app.Run();
        return exitCode;
    }
    private static async Task RunAsync(string output)
    {
        var codec = new JsonPhoneMessageCodec();
        await using var manager = new PhoneStateManager(codec);
        var testClipboard = new TestClipboard();
        using var clipboardSync = new ClipboardSyncCoordinator(manager, testClipboard, Dispatcher.CurrentDispatcher);
        using var model = new PhoneViewModel(manager, Dispatcher.CurrentDispatcher, clipboardSync);
        var flyout = new FlyoutWindow { DataContext = model };
        var mediaCard = (Border)(flyout.FindName("MediaCard") ?? throw new Exception("Media card was not created."));
        using (var tray = new TrayIconController())
        {
            Check(tray.Visible, "Native tray icon is visible");
            tray.SetDemo(true); tray.SetStatus("Sample");
        }
        await Settle();
        Check(model.Status == "Not connected" && model.Dnd == "Unavailable" && !model.PlayPause.CanExecute(null), "Disconnected presentation");
        Check(model.ClipboardStatus == "Off", "Clipboard sync defaults off");
        Check(!model.HasMedia && mediaCard.Visibility == Visibility.Collapsed, "Player hidden without an active media session");
        Render(flyout, output, "disconnected", 1);

        await manager.SetTransportAsync(new MockPhoneTransport(codec));
        await Settle();
        Check(model.Status == "Sample" && model.BatteryText == "68% · Not charging", "Sample clearly identified");
        Check(model.CellularText == "Wi-Fi · 5G · Good signal" && model.Dnd == "Off" && model.Sound == "Vibrate", "All phone status fields");
        Check(model.PlaybackLabel == "Pause" && model.PlaybackStateText == "Playing" && model.PlayPause.CanExecute(null),
            "Media playback state and controls enabled");
        Check(model.HasMedia && mediaCard.Visibility == Visibility.Visible, "Player shown for an active media session");
        Render(flyout, output, "sample-light", 1);
        Render(flyout, output, "sample-light-150pct", 1.5);
        SystemThemeService.ApplyPalette(Application.Current.Resources, AppTheme.Dark);
        await Settle();
        Check(((SolidColorBrush)Application.Current.Resources["WindowBackground"]).Color == Color.FromRgb(9, 11, 14), "Dark palette applied");
        Render(flyout, output, "sample-dark", 1);
        SystemThemeService.ApplyPalette(Application.Current.Resources, AppTheme.Light);
        await Settle();
        Check(((SolidColorBrush)Application.Current.Resources["WindowBackground"]).Color == Colors.White, "Light palette applied");
        var controls = Descendants<Button>((DependencyObject)flyout.Content).ToArray();
        Check(controls.Count(b => ReferenceEquals(b.Command, model.Previous) || ReferenceEquals(b.Command, model.PlayPause) ||
            ReferenceEquals(b.Command, model.Next)) == 3, "Exactly three media buttons are bound");
        model.ToggleClipboard.Execute(null);
        await Eventually(() => model.ClipboardEnabled);
        Check(model.ClipboardStatus == "Waiting", "Clipboard toggle waits safely without a trusted phone");

        model.PlayPause.Execute(null);
        await Eventually(() => model.PlaybackLabel == "Play" && model.PlayPause.CanExecute(null));
        Check(model.PlaybackStateText == "Paused", "Paused state is displayed");
        Check(!model.HasMedia && mediaCard.Visibility == Visibility.Collapsed, "Paused media player stays hidden");
        var first = model.Title;
        model.Next.Execute(null);
        await Eventually(() => model.Title != first && model.Next.CanExecute(null));
        model.Previous.Execute(null);
        await Eventually(() => model.Title == first && model.Previous.CanExecute(null));
        Render(flyout, output, "paused", 1);

        var live = new UiTestTransport();
        await manager.SetTransportAsync(live);
        live.Emit(codec.Encode(new StateSnapshot(new(100, true),
            new("A source with a very long name that should be clipped neatly", new string('W', 256), "An artist with a long name to check ellipsis and available space", false, new(true, false, true)),
            new(true, CellularNetwork.FourG, SignalStrength.Good, false), new(true, false, true), SoundMode.Silent)));
        await Settle();
        Check(model.Status == "Connected" && !model.IsDemo && !model.Next.CanExecute(null), "Live state and per-command capability binding");
        Check(model.Dnd == "On" && model.Sound == "Silent" && model.BatteryText == "100% · Charging", "Alternate settings and charging");
        Check(model.CanControlDndRule && model.DndRuleState == "Off", "Companion DND control is distinct from effective DND");
        Check(model.CellularText == "Mobile data · LTE · Good signal", "Mobile-data state includes cellular generation and signal");
        Check(model.ClipboardStatus == "On", "Clipboard sync becomes ready on a trusted route");
        model.ToggleDndRule.Execute(null);
        await Eventually(() => live.Sent.Any(frame => codec.Decode(frame).Message == new DndRuleCommandMessage(true)));
        Check(model.Dnd == "On" && model.DndRuleState == "Off", "DND waits for phone confirmation");
        live.Emit(codec.Encode(new DndUpdate(new(true, true, true))));
        await Eventually(() => model.DndRuleState == "On");
        model.ToggleDndRule.Execute(null);
        await Eventually(() => live.Sent.Any(frame => codec.Decode(frame).Message == new DndRuleCommandMessage(false)));
        live.Emit(codec.Encode(new DndUpdate(new(true, false, true))));
        await Eventually(() => model.DndRuleState == "Off");
        Check(model.Dnd == "On", "Turning companion rule off preserves effective DND from other sources");
        testClipboard.CopyLocally("Copied on Windows");
        await Eventually(() => live.Sent.Any(frame => codec.Decode(frame).Message is ClipboardUpdate { Content.Text: "Copied on Windows" }));
        var sentBeforeRemote = live.Sent.Count;
        live.Emit(codec.Encode(new ClipboardUpdate(new(Guid.NewGuid(), "Copied on phone"))));
        await Eventually(() => testClipboard.Text == "Copied on phone");
        await Settle();
        Check(live.Sent.Count == sentBeforeRemote, "Remote clipboard update is not echoed back");
        Render(flyout, output, "long-metadata", 1);
        live.Emit(codec.Encode(new MediaUpdate(null)));
        await Settle();
        Check(model.Title == "Nothing playing" && !model.PlayPause.CanExecute(null) && !model.HasMedia,
            "No active media disables controls");
        Check(mediaCard.Visibility == Visibility.Collapsed, "Player collapses when media ends");
        SystemThemeService.ApplyPalette(Application.Current.Resources, AppTheme.Dark);
        await Settle();
        Render(flyout, output, "no-media-dark", 1);
        await manager.SetTransportAsync(null);
        await Settle();
        Check(model.Dnd == "Unavailable" && model.Sound == "Unavailable" && !model.Next.CanExecute(null), "Disconnect resets display");
        flyout.CloseForExit();
        Console.WriteLine($"UI smoke checks passed. Rendered flyouts: {output}");
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); }
    private static async Task Settle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static async Task Eventually(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!condition()) { await Task.Delay(20, timeout.Token); await Settle(); }
    }
    private static void Render(FlyoutWindow flyout, string output, string name, double scale)
    {
        var content = (FrameworkElement)flyout.Content;
        content.Measure(new Size(flyout.Width, double.PositiveInfinity));
        var size = new Size(flyout.Width, content.DesiredSize.Height);
        content.Arrange(new Rect(new Point(0, 0), size));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
        Check(size.Width <= 368 && size.Height <= 600, $"{name}: compact layout {size.Width:0} × {size.Height:0}");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class UiTestTransport : IPhoneTransport
    {
        public TransportKind Kind => TransportKind.Wifi;
        public ConnectionState State { get; private set; }
        public event Action<ReadOnlyMemory<byte>>? FrameReceived;
        public event Action<ConnectionState>? ConnectionChanged;
        public event Action<Exception>? Faulted { add { } remove { } }
        public List<byte[]> Sent { get; } = [];
        public Task StartAsync(CancellationToken cancellationToken = default)
        { State = ConnectionState.Connected; ConnectionChanged?.Invoke(State); return Task.CompletedTask; }
        public void Emit(byte[] bytes) => FrameReceived?.Invoke(bytes);
        public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
        { Sent.Add(frame.ToArray()); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default)
        { State = ConnectionState.Disconnected; ConnectionChanged?.Invoke(State); return Task.CompletedTask; }
        public async ValueTask DisposeAsync() => await StopAsync();
    }
    private sealed class TestClipboard : IWindowsClipboard
    {
        private bool _monitoring;
        public string? Text { get; private set; }
        public event Action<string>? TextChanged;
        public void StartMonitoring() => _monitoring = true;
        public void StopMonitoring() => _monitoring = false;
        public void CopyLocally(string text) { Text = text; if (_monitoring) TextChanged?.Invoke(text); }
        public bool TrySetText(string text)
        {
            Text = text;
            if (_monitoring) TextChanged?.Invoke(text);
            return true;
        }
        public void Dispose() => _monitoring = false;
    }
}
