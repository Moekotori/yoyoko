using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Chat.App;
using Chat.Core.Instances;
using Chat.Core.Messaging;
using Chat.Core.Sessions;
using Chat.Core.Voice;
using Chat.Domain.Instances;
using Chat.Protocol;
using Chat.UI.Instances;
using Chat.UI.Shell;

var cacheDirectory = Path.Combine(Path.GetTempPath(), "yoyoko-ultralight-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("CHAT_CACHE_DIRECTORY", cacheDirectory);
Environment.SetEnvironmentVariable("CHAT_DEFAULT_INSTANCE_URL", "http://127.0.0.1:1");
var builder = AppBuilder.Configure<DesktopApplication>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia()
    .SetupWithClassicDesktopLifetime([], lifetime => lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown);
var lifetime = (IClassicDesktopStyleApplicationLifetime)builder.Instance!.ApplicationLifetime!;
var window = (MainWindow)lifetime.MainWindow!;
window.Show();
Pump(250);
var shell = (ShellViewModel)window.DataContext!;
Check(!shell.UltraLightEnabled, "default off");
CheckOff(window, shell);

// A real VoiceRuntime with instrumented transport/media ports; no physical device or server.
var user = new UserDto(Guid.NewGuid(), "test", "test");
var channel = Guid.NewGuid();
var connects = 0;
var leaves = 0;
var api = Proxy<IChatApi>((method, _) => method.Name switch
{
    "SetAccessToken" => null,
    "JoinVoiceAsync" => Task.FromResult(new VoiceJoinDto(
        new("test", new Uri("wss://localhost/rtc"), "test", DateTimeOffset.UtcNow.AddMinutes(1)),
        new(user.Id, Guid.NewGuid(), channel, false, false, "test", AudioQualities.Standard),
        new(AudioQualities.Standard, 48000, 1, 64000, 20, true, true), AudioQualities.Standard)),
    _ => throw new InvalidOperationException("Unexpected API call: " + method.Name)
});
var media = Proxy<IVoiceMedia>((method, _) =>
{
    if (method.Name == "ConnectAsync") connects++;
    else if (method.Name == "LeaveAsync") leaves++;
    else throw new InvalidOperationException("Unexpected media call: " + method.Name);
    return Task.CompletedTask;
});
var descriptor = new InstanceDescriptor(new InstanceId(Guid.NewGuid()), new Uri("http://127.0.0.1:1"), "test");
var session = new InstanceSession(descriptor, user, api, Proxy<IMessageCache>((m, _) => throw new Exception(m.Name)),
    Proxy<ICredentialVault>((m, _) => throw new Exception(m.Name)), () => throw new Exception("Gateway recreated"), media, "test", "test");
session.Voice.JoinAsync(channel, CancellationToken.None).GetAwaiter().GetResult();
var context = new InstanceContext(descriptor);
context.AttachSession(session);
shell.SelectedInstance = new InstanceItem(context);
shell.Draft = "preserved draft";
using var file = new MemoryStream([1, 2, 3]);
shell.QueueFilesAsync([new PickedFile("test.txt", "text/plain", file, 3)]).GetAwaiter().GetResult();
shell.Settings.UltraLightEnabled = true;
shell.OpenSettings.Execute(null);
Pump(280);
CheckSettingsLayout(window);
using (var stored = JsonDocument.Parse(File.ReadAllText(Path.Combine(cacheDirectory, "preferences.json"))))
    Check(stored.RootElement.GetProperty("ultra_light_enabled").GetBoolean(), "preference persisted");

var weak = Park(window);
Pump(350); // Flush queued detach/layout/motion work before testing collectability.
Check(window.Content is null && shell.IsUltraLightParked, "enabled unloads actual ShellSurface");
Check(shell.Messages.Count == 0 && shell.VisibleMessages.Count == 0, "UI projections trimmed");
// Test-only collection verifies ownership; the product never forces GC.
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
Check(!weak.IsAlive, "old ShellSurface is collectable");
Check(ReferenceEquals(shell.SelectedInstance!.Context.Session, session) && session.Voice.Joined
    && session.Voice.ChannelId == channel && connects == 1 && leaves == 0, "voice remains joined without reconnect or leave");
Check(shell.Draft == "preserved draft" && shell.PendingFiles.Count == 1 && file.CanRead, "draft and attachment stream retained");
window.WindowState = WindowState.Normal;
Pump(280);
Check(window.Content is ShellSurface && !shell.IsUltraLightParked, "restore rebuilds actual view");
CheckSettingsLayout(window);
Check(ReferenceEquals(window.DataContext, shell) && connects == 1 && leaves == 0, "restore preserves session owner");
window.Hide();
Check(window.Content is null, "hide also unloads");
shell.Settings.UltraLightEnabled = false;
Check(window.Content is null, "turning off while hidden does not build unseen UI");
window.Show();
Pump(120);
Check(window.Content is ShellSurface, "restore after disabling");
window.WindowState = WindowState.Minimized;
Check(window.Content is ShellSurface, "disabled again keeps normal residency");
Check(connects == 1 && leaves == 0 && !session.Voice.SelfMute && !session.Voice.SelfDeaf, "voice flags unchanged through all transitions");
if (args is ["--screenshot", var screenshot])
{
    window.WindowState = WindowState.Normal;
    if (!shell.ShowSettings) shell.OpenSettings.Execute(null);
    shell.Settings.UltraLightEnabled = true;
    Pump(250);
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No settings frame");
    frame.Save(screenshot, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
}
window.Close();
Console.WriteLine("UltraLight: real view unload/collection/restore, preference persistence, draft/file and voice control continuity passed. Headless; no hardware/RTP claim.");

static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
{
    var proxy = DispatchProxy.Create<T, PortProxy>();
    ((PortProxy)(object)proxy).Handler = handler;
    return proxy;
}
[MethodImpl(MethodImplOptions.NoInlining)]
static void CheckSettingsLayout(Window window)
{
    var slot = window.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("channelSlot"));
    var deadline = Environment.TickCount64 + 1000;
    while ((slot.Bounds.Width >= 1 || slot.Opacity >= 0.01) && Environment.TickCount64 < deadline)
        Pump(5);
    Check(slot.Bounds.Width < 1 && slot.Opacity < 0.01, "settings collapses channel slot before and after restore");
}
[MethodImpl(MethodImplOptions.NoInlining)]
static void CheckOff(Window window, ShellViewModel shell)
{
    var original = window.Content;
    window.WindowState = WindowState.Minimized;
    Check(ReferenceEquals(original, window.Content) && !shell.IsUltraLightParked, "off retains the view");
    window.WindowState = WindowState.Normal;
}
[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference Park(Window window)
{
    var weak = new WeakReference(window.Content);
    window.WindowState = WindowState.Minimized;
    return weak;
}
static void Pump(int milliseconds)
{
    var end = Environment.TickCount64 + milliseconds;
    do { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(1); } while (Environment.TickCount64 < end);
}
static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
public class PortProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
}
