namespace RobloxLaunchHelper;

// Owned controls, not additional top-level windows. Awaiting keeps the UI responsive.
internal static class WorkspacePrompt
{
    internal static async Task<bool> ConfirmAsync(Control owner, string message, string title)
        => await ShowAsync(owner, title, message, null) is not null;
    internal static Task<string?> NameAsync(Control owner, string title, string initial)
        => ShowAsync(owner, title, "Preset name", initial);
    private static async Task<string?> ShowAsync(Control owner, string title, string message, string? initial)
    {
        var host = owner.FindForm() ?? throw new InvalidOperationException("Prompt requires an owning workspace.");
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Control? previous = host.ActiveControl;
        using var overlay = new PromptOverlay { Dock = DockStyle.Fill, Padding = new(40), AccessibleName = title };
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, RowCount = 5, Padding = new(28) };
        card.RowStyles.Add(new(SizeType.AutoSize)); card.RowStyles.Add(new(SizeType.Percent,100));
        card.RowStyles.Add(new(SizeType.AutoSize)); card.RowStyles.Add(new(SizeType.AutoSize)); card.RowStyles.Add(new(SizeType.AutoSize));
        card.Controls.Add(new Label { Text = title, AutoSize = true, Font = new("Segoe UI",18,FontStyle.Bold) },0,0);
        var body = new Label { Text = message, AutoSize = true, Dock = DockStyle.Top };
        body.MaximumSize = new(Math.Max(200,host.ClientSize.Width-160),0); card.Controls.Add(body,0,1);
        var box = new TextBox { Text = initial ?? "", MaxLength = 80, Dock = DockStyle.Top, AccessibleName = "Preset name", Visible = initial is not null };
        card.Controls.Add(box,0,2);
        var error = new Label { AutoSize = true }; card.Controls.Add(error,0,3);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        var cancel = WorkspaceControls.Button("Cancel", () => completion.TrySetResult(null));
        var accept = WorkspaceControls.Button(initial is null ? "Confirm" : "Save", () =>
        {
            if (initial is not null && (string.IsNullOrWhiteSpace(box.Text) || box.Text.Any(char.IsControl)))
            { error.Text = "Enter a name without control characters."; return; }
            completion.TrySetResult(initial is null ? "confirmed" : box.Text.Trim());
        });
        actions.Controls.Add(cancel); actions.Controls.Add(accept); card.Controls.Add(actions,0,4); overlay.Controls.Add(card);
        overlay.Cancel = () => completion.TrySetResult(null);
        overlay.Accept = () => accept.PerformClick();
        var disabled = host.Controls.Cast<Control>().Where(c=>c.Enabled).ToArray();
        foreach (var control in disabled) control.Enabled = false;
        FormClosedEventHandler closed = (_,_)=>completion.TrySetResult(null);
        host.FormClosed += closed;
        host.Controls.Add(overlay); overlay.BringToFront();
        ThemePalette.Resolve((host as DiagnosticsForm)?.Scheme ?? ColorScheme.Dark).Apply(overlay);
        overlay.TabIndex = 0; if (initial is null) cancel.Focus(); else box.Focus();
        try { return await completion.Task; }
        finally
        {
            host.FormClosed -= closed;
            if (!host.IsDisposed)
            {
                host.Controls.Remove(overlay);
                foreach (var control in disabled) if (!control.IsDisposed) control.Enabled = true;
                previous?.Focus();
            }
        }
    }
    private sealed class PromptOverlay : ContainerControl
    {
        internal Action? Cancel, Accept;
        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            if (keys == Keys.Escape) { Cancel?.Invoke(); return true; }
            if (keys == Keys.Enter) { Accept?.Invoke(); return true; }
            if (keys is Keys.Tab or (Keys.Shift | Keys.Tab))
            { SelectNextControl(ActiveControl,keys==Keys.Tab,true,true,true); return true; }
            return base.ProcessCmdKey(ref message,keys);
        }
    }
}
