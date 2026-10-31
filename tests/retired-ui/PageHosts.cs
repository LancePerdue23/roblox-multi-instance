using RobloxLaunchHelper;

// Standalone hosts retain existing focused control tests; production uses the shared shell.
internal class PageHost<T> : Form where T : WorkspacePage
{
    internal T Page { get; }
    internal PageHost(T page)
    {
        Page = page; ShowInTaskbar = false; Opacity = 0; Size = new(1120,780); AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(page); page.CloseRequested += Close;
    }
}
internal sealed class ActiveInstancesForm : PageHost<ActiveInstancesPage>
{
    internal ActiveInstancesForm(bool collectOnShown = true, IInstancePlatform? platform = null, LayoutPreferences? preferences = null,
        Action? enableTray = null, Action? configureShortcuts = null) : base(new(collectOnShown,platform,preferences,enableTray,configureShortcuts)) { }
    internal InstanceTarget? SelectedTarget => Page.SelectedTarget;
    internal void Present(InstanceSnapshot snapshot) => Page.Present(snapshot);
}
internal sealed class ArrangementForm : PageHost<ArrangementPage>
{
    internal ArrangementForm(List<ArrangementChoice> choices, LayoutPreferences preferences, ArrangementKind layout,
        string? presetName, string? initialMonitor, IPlacementPlatform? platform = null,
        Func<CancellationToken, InstanceSnapshot>? discover = null) : base(new(choices,preferences,layout,presetName,initialMonitor,platform,discover)) { }
    internal Task RefreshPreviewAsync() => Page.RefreshPreviewAsync();
}
internal sealed class NotificationSettingsForm : PageHost<NotificationSettingsPage>
{
    internal NotificationSettingsForm(NotificationPreferences? preferences = null) : base(new(preferences)) { }
}
internal sealed class ShortcutSettingsForm : PageHost<ShortcutSettingsPage>
{
    internal ShortcutSettingsForm(LayoutPreferences preferences) : base(new(preferences)) { }
}
