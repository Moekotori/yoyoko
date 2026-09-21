using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Chat.Motion;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var childTransform = new ScaleTransform(0.95, 0.95);
var host = new MotionHost { Child = new Border { Width = 100, Height = 40, RenderTransform = childTransform } };
var parent = new Border { Child = host };
var window = new Window { Content = parent, Width = 320, Height = 240 };
window.Show();
Pump(260);
Settled("initial entry");
var layout = host.Bounds;
host.Play();
Pump(40);
Check(host.IsRunning && host.Opacity > 0 && host.Opacity < 1, "intermediate opacity");
Check(((TranslateTransform)host.RenderTransform!).Y > 0, "intermediate translation");
Check(host.Bounds == layout && ReferenceEquals(host.Child.RenderTransform, childTransform), "stable layout and child transform");

var before = host.Opacity;
for (var i = 0; i < 100; i++) host.Trigger = i;
Dispatcher.UIThread.RunJobs();
Check(host.Opacity >= before - 0.01, "interruption must not restart from invisible");
Pump(260);
Settled("rapid trigger replacement");

host.Play();
Pump(30);
Motion.SetReduceMotion(window, true);
Dispatcher.UIThread.RunJobs();
Settled("inherited reduced motion cancels active animation");
host.Play();
Pump(20);
Settled("reduced motion bypass");
Motion.SetReduceMotion(window, false);

host.Play();
Pump(30);
parent.IsVisible = false;
Dispatcher.UIThread.RunJobs();
Settled("ancestor hiding");
host.Play();
Pump(20);
Settled("hidden request bypass");
parent.IsVisible = true;
Pump(30);
Check(host.IsRunning, "showing ancestor triggers entry");
window.WindowState = WindowState.Minimized;
Dispatcher.UIThread.RunJobs();
Settled("minimizing");
host.Play();
Pump(20);
Settled("minimized request bypass");
window.WindowState = WindowState.Normal;

host.Play();
host.Stop();
Pump(20);
Settled("cancel queued request");
host.Play();
Pump(30);
parent.Child = null;
Dispatcher.UIThread.RunJobs();
Settled("detach cancels active animation");
parent.Child = host;
Pump(260);
Settled("reattach");
foreach (var preset in new[] { MotionPreset.None, MotionPreset.Fade, MotionPreset.SlideLeft, MotionPreset.SlideRight })
{
    host.Preset = preset;
    host.Play();
    Pump(30);
    if (preset == MotionPreset.SlideLeft) Check(((TranslateTransform)host.RenderTransform!).X > 0, "left direction");
    if (preset == MotionPreset.SlideRight) Check(((TranslateTransform)host.RenderTransform!).X < 0, "right direction");
    Pump(240);
    Settled(preset.ToString());
}
host.Preset = MotionPreset.Fade;
host.StartOpacity = 0.82;
host.Duration = TimeSpan.FromMilliseconds(120);
host.Play();
Pump(25);
Check(host.IsRunning && host.Opacity >= 0.82 && host.Opacity < 1, "channel content stays visible during feedback");
var channelOpacity = host.Opacity;
for (var i = 0; i < 20; i++) host.Trigger = new object();
Dispatcher.UIThread.RunJobs();
Check(host.Opacity >= channelOpacity - 0.01, "rapid channel feedback does not flash");
Pump(180);
Settled("channel content feedback");
host.Duration = TimeSpan.Zero;
host.Play();
Pump(20);
Settled("zero duration");
window.Close();
Console.WriteLine("Motion checks passed: real clock interpolation, interruption, layout, policy, visibility, minimize, detach and presets.");

void Settled(string context)
{
    var transform = (TranslateTransform)host.RenderTransform!;
    Check(!host.IsRunning && Math.Abs(host.Opacity - 1) < 0.001 &&
        Math.Abs(transform.X) < 0.001 && Math.Abs(transform.Y) < 0.001, $"{context}: running={host.IsRunning}, opacity={host.Opacity}, x={transform.X}, y={transform.Y}");
}
static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}
static void Pump(int milliseconds)
{
    var frame = new DispatcherFrame();
    using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(milliseconds));
    Dispatcher.UIThread.PushFrame(frame);
    Dispatcher.UIThread.RunJobs();
}
