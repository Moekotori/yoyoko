using Avalonia.Controls;
using Chat.UI.Resources;

internal static class RecoveryChecks
{
    public static void Run()
    {
        var owner = new object();
        var window = new Window { DataContext = owner, Content = new Border() };
        window.Show();
        var failBuild = true;
        var failApply = false;
        var failRecoveryView = false;
        var hideDuringBuild = false;
        var attempts = 0;
        var parked = false;
        using var content = new UltraLightWindowContent(window, () =>
        {
            attempts++;
            if (failBuild) throw new InvalidOperationException("injected build failure");
            if (hideDuringBuild) window.Hide();
            return new Border();
        }, value =>
        {
            parked = value;
            if (!value && failApply) throw new InvalidOperationException("injected projection failure");
        }, retry =>
        {
            if (failRecoveryView) throw new InvalidOperationException("injected recovery view failure");
            var button = new Button { Content = "Retry" };
            button.Click += (_, _) => retry();
            return button;
        });
        content.SetEnabled(true);
        window.WindowState = WindowState.Minimized;
        window.WindowState = WindowState.Normal;
        Require(content.IsParked && parked && window.Content is Button && content.LastRestoreError is not null,
            "factory failure leaves a retryable view and parked state");
        content.SetEnabled(true);
        Require(attempts == 1, "failed recovery does not loop on unrelated changes");
        failBuild = false;
        failApply = true;
        content.RetryRestore();
        Require(content.IsParked && parked && window.Content is Button, "projection failure rolls back restore state");
        failApply = false;
        content.RetryRestore();
        Require(!content.IsParked && !parked && window.Content is Border && content.LastRestoreError is null,
            "manual retry recovers without replacing the business owner");
        window.WindowState = WindowState.Minimized;
        Require(window.Content is Border, "fault fuse suspends further deep unload");
        window.WindowState = WindowState.Normal;
        content.SetEnabled(false);
        content.SetEnabled(true);
        window.WindowState = WindowState.Minimized;
        Require(content.IsParked && window.Content is null, "explicit off/on re-enables parking");
        hideDuringBuild = true;
        window.WindowState = WindowState.Normal;
        Require(content.IsParked && parked && window.Content is null, "a stale restore cannot attach after re-hide");
        hideDuringBuild = false;
        window.Show();
        Require(!content.IsParked && window.Content is Border && ReferenceEquals(window.DataContext, owner),
            "next show recovers after interrupted restore");
        failBuild = true;
        failRecoveryView = true;
        window.WindowState = WindowState.Minimized;
        window.WindowState = WindowState.Normal;
        Require(content.IsParked && window.Content is Button, "even a failed recovery view retains a basic retry button");
        failBuild = false;
        failRecoveryView = false;
        content.RetryRestore();
        Require(!content.IsParked && ReferenceEquals(window.DataContext, owner), "fallback retry preserves the business owner");
        window.Close();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
