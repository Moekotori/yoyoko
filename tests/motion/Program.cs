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
Pump(400);
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
foreach (var preset in new[] { MotionPreset.None, MotionPreset.Fade, MotionPreset.SlideLeft, MotionPreset.SlideRight, MotionPreset.SlideUp, MotionPreset.SlideDown })
{
    host.Preset = preset;
    host.Play();
    Pump(30);
    if (preset == MotionPreset.SlideLeft) Check(((TranslateTransform)host.RenderTransform!).X > 0, "left direction");
    if (preset == MotionPreset.SlideRight) Check(((TranslateTransform)host.RenderTransform!).X < 0, "right direction");
    if (preset == MotionPreset.SlideUp) Check(((TranslateTransform)host.RenderTransform!).Y > 0, "up direction");
    if (preset == MotionPreset.SlideDown) Check(((TranslateTransform)host.RenderTransform!).Y < 0, "down direction");
    Pump(240);
    Settled(preset.ToString());
}
host.Preset = MotionPreset.Fade;
host.Trigger = "same-turn-a";
Pump(260);
host.Preset = MotionPreset.SlideUp;
host.Trigger = "same-turn-b";
Dispatcher.UIThread.RunJobs();
Pump(30);
Check(((TranslateTransform)host.RenderTransform!).Y > 0 && host.IsRunning, "same-turn preset is used by the pending trigger");
Pump(240);
Settled("same-turn preset and trigger");
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

var reveal = new RevealHost { IsOpen = true, Child = new Border { Width = 120, Height = 80 } };
var below = new Border { Width = 120, Height = 16 };
var column = new StackPanel();
column.Children.Add(reveal);
column.Children.Add(below);
var revealWindow = new Window { Content = column, Width = 240, Height = 200 };
revealWindow.Show();
Pump(240);
Check(!reveal.IsRunning && Math.Abs(reveal.Progress - 1) < 0.001 && reveal.Bounds.Height > 70, "reveal starts open without motion");
var openBelow = below.Bounds.Y;
reveal.IsOpen = false;
Pump(40);
Check(reveal.IsRunning && reveal.Progress > 0 && reveal.Progress < 1 && reveal.Bounds.Height > 0 && reveal.Bounds.Height < 80, "reveal collapse interpolates height");
Check(below.Bounds.Y < openBelow, "reveal height change moves siblings");
var mid = reveal.Progress;
reveal.IsOpen = true;
Dispatcher.UIThread.RunJobs();
Pump(30);
Check(reveal.Progress >= mid - 0.02, "reveal reverse continues from current height");
Pump(280);
Check(!reveal.IsRunning && Math.Abs(reveal.Progress - 1) < 0.001 && reveal.Bounds.Height > 70, "reveal reopened");
reveal.IsOpen = false;
Pump(30);
Motion.SetReduceMotion(revealWindow, true);
Dispatcher.UIThread.RunJobs();
Check(!reveal.IsRunning && reveal.Progress < 0.001 && reveal.Bounds.Height < 1, "reveal reduced motion snaps closed");
Motion.SetReduceMotion(revealWindow, false);

var closed = new RevealHost { IsOpen = false, Child = new Border { Width = 100, Height = 80 } };
var closedColumn = new StackPanel();
closedColumn.Children.Add(closed);
var closedWindow = new Window { Content = closedColumn, Width = 200, Height = 160 };
closedWindow.Show();
Pump(200);
Check(!closed.IsRunning && closed.Progress < 0.001 && closed.Bounds.Height < 1, "closed reveal starts at zero height");
closed.IsOpen = true;
Pump(40);
Check(closed.IsRunning && closed.Progress > 0 && closed.Progress < 1 && closed.Bounds.Height > 0, "reveal expand interpolates height");
Pump(280);
Check(!closed.IsRunning && Math.Abs(closed.Progress - 1) < 0.001, "reveal expand settled");
closedWindow.Close();
revealWindow.Close();
Console.WriteLine("Motion checks passed: real clock interpolation, interruption, layout, policy, visibility, minimize, detach, presets and reveal.");

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
