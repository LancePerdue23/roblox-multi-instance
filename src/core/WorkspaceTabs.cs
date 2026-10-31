namespace RobloxLaunchHelper;

// An embedded page strip avoids the native light tab frame in dark/high-contrast themes.
internal sealed class WorkspaceTabs : UserControl
{
    private readonly FlowLayoutPanel strip = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new(0,0,0,18) };
    private readonly Panel body = new() { Dock = DockStyle.Fill };
    private readonly List<Control> pages = [];
    private readonly List<TabButton> buttons = [];
    private int selectedIndex;
    internal event EventHandler? SelectedIndexChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value<0 || value>=pages.Count || value==selectedIndex) return;
            selectedIndex=value; UpdateSelection(); SelectedIndexChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    internal WorkspaceTabs() { Dock=DockStyle.Fill; Controls.Add(body); Controls.Add(strip); }
    internal void AddPage(string title,Control content)
    {
        int index=pages.Count;
        // Each page owns overflow; a second scrolling viewport can oscillate during docking.
        var page = new Panel { Dock=DockStyle.Fill, Padding=new(0), AutoScroll=false };
        page.Controls.Add(content); pages.Add(page); body.Controls.Add(page);
        var button=new TabButton { Text=title, AccessibleName=title, AutoSize=true, MinimumSize=new(135,48), UseMnemonic=false, Margin=new(0,0,8,0), TabIndex=index };
        button.Click += (_,_)=>SelectedIndex=index; buttons.Add(button); strip.Controls.Add(button); UpdateSelection();
    }
    private void UpdateSelection()
    {
        for(int i=0;i<pages.Count;i++) { pages[i].Visible=i==selectedIndex; buttons[i].Selected=i==selectedIndex; buttons[i].Invalidate(); }
    }
    protected override bool ProcessCmdKey(ref Message message,Keys keys)
    {
        if(strip.ContainsFocus && keys is Keys.Left or Keys.Right)
        { SelectedIndex=(selectedIndex+(keys==Keys.Right?1:pages.Count-1))%pages.Count; buttons[selectedIndex].Focus(); return true; }
        return base.ProcessCmdKey(ref message,keys);
    }
    private sealed class TabButton : WorkspaceButton
    {
        internal bool Selected;
        protected override void OnPaint(PaintEventArgs e)
        {
            var theme=ThemePalette.For(this);
            e.Graphics.Clear(Parent?.BackColor ?? theme.Background);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Selected?theme.Accent:theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if(Selected) { using var pen=new Pen(theme.Accent,3); e.Graphics.DrawLine(pen,8,Height-3,Width-8,Height-3); }
            if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4),theme.Text,theme.Surface);
        }
    }
}
