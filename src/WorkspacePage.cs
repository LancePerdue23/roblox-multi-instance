namespace RobloxLaunchHelper;

internal enum WorkspaceSection { Instances, Layouts, Activity, System, Settings }
internal enum SystemSection { Health, Updates, Reports }
internal enum SettingsSection { Appearance, Notifications, Shortcuts }
internal readonly record struct WorkspaceTarget(WorkspaceSection Page, SystemSection System = SystemSection.Health,
    SettingsSection Settings = SettingsSection.Appearance);
internal interface IWorkspaceNavigation { Task NavigateAsync(WorkspaceTarget target); }

// Pages own their controls and cancellation; the shell owns their lifetime.
internal class WorkspacePage : UserControl
{
    internal event Action? CloseRequested;
    internal WorkspacePage() { Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.Dpi; }
    protected void Close() => CloseRequested?.Invoke();
}

internal sealed class MessageDispatcher : Control
{
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.Parent = new nint(-3); return parameters; }
    }
}

internal static class WorkspaceControls
{
    internal static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c)));
    internal static Button Button(string text, Action action)
    {
        var button = new WorkspaceButton { Text = text, AutoSize = true, AccessibleName = text.Replace("&", ""), Margin = new(4) };
        button.Click += (_, _) => action(); return button;
    }
}
