using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Chat.Core.Messaging;
using Chat.Protocol;
using Chat.UI.Chat;
using Chat.UI.Resources;
using Chat.UI.Components;
using SkiaSharp;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions
    { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
var png = Png(128, 128);
using var previews = new AttachmentPreviews(CancellationToken.None);
var row = Row(1);
var duplicate = Row(1);
var pending = new TaskCompletionSource<byte[]?>();
var downloads = 0;
Task<byte[]?> Delayed(Uri url, int maxBytes, CancellationToken token)
{
    downloads++;
    return pending.Task;
}
for (var i = 0; i < 20; i++) previews.Update([row, duplicate], Delayed);
Check(downloads == 1, "refreshes and duplicate URLs share one download");
pending.SetResult(png);
PumpUntil(() => row.HasPreview);
Check(ReferenceEquals(row.Preview, duplicate.Preview), "rows share the bitmap");
Check(previews.RetainedBytes == 128 * 128 * 4, "borrowed images stay accounted");
previews.Update([duplicate], Delayed);
Check(row.Preview is null && duplicate.HasPreview, "removed row releases its reference");
previews.Clear();
Check(duplicate.Preview is null && previews.RetainedBytes == 0, "clear detaches all consumers");

pending = new();
previews.Update([row], Delayed);
previews.Clear();
pending.SetResult(png); // Simulate a transport which completes despite cancellation.
Dispatcher.UIThread.RunJobs();
Check(!row.HasPreview && previews.RetainedBytes == 0, "late completion cannot resurrect a cleared scope");

var gates = new List<TaskCompletionSource<byte[]?>>();
var rows = Enumerable.Range(1, 20).Select(Row).ToArray();
previews.Update(rows, (_, _, token) =>
{
    var gate = new TaskCompletionSource<byte[]?>();
    gates.Add(gate);
    token.Register(() => gate.TrySetCanceled());
    return gate.Task;
});
Check(gates.Count == 4, "pending work has four workers, not one task per row");
previews.Clear();
Dispatcher.UIThread.RunJobs();
Check(gates.Count == 4, "cancel does not start the abandoned queue");

rows = Enumerable.Range(1, 300).Select(Row).ToArray();
downloads = 0;
previews.Update(rows, (_, _, _) => { downloads++; return Task.FromResult<byte[]?>(png); });
Check(previews.RetainedBytes == MemoryBudget.ThumbnailBytes, "retained previews obey 16 MiB budget");
Check(rows.Count(item => item.HasPreview) == 256, "overflow keeps attachment chips");
Check(downloads == 256, "full budget stops new downloads");
previews.SetBudget(2 * 1024 * 1024, 1);
Check(previews.RetainedBytes == 2 * 1024 * 1024 && rows.Count(item => item.HasPreview) == 32, "pressure evicts borrowed images too");
Check(downloads == 256, "budget shrink does not redownload evicted images");
previews.SetBudget(0, 0);
Check(previews.RetainedBytes == 0, "hidden budget releases all thumbnails");
previews.Update(rows, (_, _, _) => throw new Exception("hidden download must not run"));
Check(rows.All(item => !item.HasPreview), "hidden refresh does not restart downloads");
previews.SetBudget(MemoryBudget.ThumbnailBytes, 4);
previews.Clear();
Check(rows.All(item => !item.HasPreview), "budgeted consumers detached before disposal");

var portrait = Png(16, 2048);
previews.Update([row], (_, _, _) => Task.FromResult<byte[]?>(portrait));
Check(row.Preview is { PixelSize.Width: <= 128, PixelSize.Height: <= 128 }, "portrait bounds both axes");
previews.Dispose();
Check(!row.HasPreview && previews.RetainedBytes == 0, "dispose releases final image");
using (var avatar = AvatarPlayback.Decode(portrait, 128, allowAnimation: false))
    Check(avatar.Current.PixelSize.Height <= 128 && avatar.DecodedBytes <= 65536, "static avatar is bounded");
var policy = new VisualResourcePolicy();
Check(policy.Resolve(true, true, TimeSpan.Zero) == VisualResourceBudget.Active, "foreground budget");
Check(policy.Resolve(true, false, TimeSpan.Zero) == VisualResourceBudget.Idle, "blur budget");
Check(policy.Resolve(true, true, TimeSpan.FromSeconds(61)) == VisualResourceBudget.Idle, "idle budget");
policy.Sample(TimeSpan.Zero, 270L * 1024 * 1024, 0.5);
Check(policy.Resolve(true, true, TimeSpan.Zero) == VisualResourceBudget.Pressure, "RSS pressure");
Check(policy.Resolve(false, true, TimeSpan.Zero) == VisualResourceBudget.Hidden, "hidden overrides pressure");
policy.Sample(TimeSpan.FromSeconds(15), 200L * 1024 * 1024, 0.5);
policy.Sample(TimeSpan.FromSeconds(30), 200L * 1024 * 1024, 0.5);
Check(policy.Resolve(true, true, TimeSpan.Zero) == VisualResourceBudget.Pressure, "no oscillating recovery");
policy.Sample(TimeSpan.FromSeconds(45), 200L * 1024 * 1024, 0.5);
Check(policy.Resolve(true, true, TimeSpan.Zero) == VisualResourceBudget.Active, "sustained recovery");
policy.Sample(TimeSpan.FromSeconds(60), 100L * 1024 * 1024, 0.95);
Check(policy.Resolve(true, true, TimeSpan.Zero) == VisualResourceBudget.Pressure, "system load pressure");
var window = new Avalonia.Controls.Window();
window.Show();
VisualResourceBudget? applied = null;
using (var governor = new WindowResourceGovernor(window, budget => applied = budget))
{
    window.WindowState = Avalonia.Controls.WindowState.Minimized;
    Check(applied?.Suspended == true, "window minimize applies hidden budget");
    window.WindowState = Avalonia.Controls.WindowState.Normal;
    Check(applied?.Suspended == false, "window restore resumes budget");
    window.Hide();
    Check(applied?.Suspended == true, "window hide applies hidden budget");
}
window.Close();
Console.WriteLine("Preview ownership, real Skia decoding, dynamic budgets and pressure hysteresis passed (headless, not desktop RSS).");

static AttachmentRow Row(int id) => new(new AttachmentDto(Guid.CreateVersion7(), "image.png", "image/png", 100,
    new Uri($"http://localhost/files/{id}"), new Uri($"http://localhost/thumb/{id}")), _ => Task.CompletedTask);
static byte[] Png(int width, int height)
{
    using var bitmap = new SKBitmap(width, height);
    bitmap.Erase(SKColors.CornflowerBlue);
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}
static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
static void PumpUntil(Func<bool> predicate)
{
    var deadline = DateTime.UtcNow.AddSeconds(3);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(1);
    }
    Check(predicate(), "async image completion");
}
