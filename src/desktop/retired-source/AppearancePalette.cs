using Microsoft.Win32;
using System.Runtime.CompilerServices;
namespace RobloxLaunchHelper;
internal sealed record ThemePalette(Color Background, Color Surface, Color Text, Color Muted, Color Border, Color Accent, Color Selection)
{
    private sealed class AppliedPalette(ThemePalette value) { internal ThemePalette Value = value; }
    private static readonly ConditionalWeakTable<Control, AppliedPalette> appliedPalettes = new();
    private static readonly ThemePalette fallback = Resolve(ColorScheme.Dark);
    internal static ThemePalette For(Control control) => appliedPalettes.TryGetValue(control, out var applied)
        ? applied.Value : (control.FindForm() as DiagnosticsForm)?.Palette ?? fallback;

    internal static ThemePalette Resolve(ColorScheme scheme)
    {
        if (SystemInformation.HighContrast) return new(SystemColors.Window, SystemColors.Control, SystemColors.WindowText,
            SystemColors.ControlText, SystemColors.WindowFrame, SystemColors.Highlight, SystemColors.Highlight);
        if (scheme == ColorScheme.FollowWindows)
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                scheme = key?.GetValue("AppsUseLightTheme") is int light && light == 1 ? ColorScheme.Light : ColorScheme.Dark; }
            catch { scheme = ColorScheme.Dark; }
        }
        return scheme switch
        {
            ColorScheme.Light => new(Color.White, Color.White, Color.FromArgb(25,32,44), Color.FromArgb(78,91,110), Color.FromArgb(212,219,229), Color.FromArgb(0,100,210), Color.FromArgb(220,235,254)),
            ColorScheme.WarmNeutral => new(Color.FromArgb(248,247,244), Color.FromArgb(253,252,249), Color.FromArgb(34,40,43), Color.FromArgb(90,96,98), Color.FromArgb(212,213,207), Color.FromArgb(0,116,120), Color.FromArgb(209,233,229)),
            _ => new(Color.FromArgb(20,25,33), Color.FromArgb(28,35,45), Color.FromArgb(237,242,249), Color.FromArgb(169,184,204), Color.FromArgb(51,64,81), Color.FromArgb(33,138,246), Color.FromArgb(37,65,95))
        };
    }
    internal void Apply(Control root)
    {
        root.SuspendLayout();
        try { ApplyCore(root); }
        finally { root.ResumeLayout(true); }
    }
    private void ApplyCore(Control root)
    {
        appliedPalettes.GetValue(root, _ => new AppliedPalette(this)).Value = this;
        root.BackColor = root.Tag as string == "sidebar" ? (Text.R>150 ? Surface : Color.FromArgb(241,243,246)) : root is Form or WorkspacePage ? Background : root is WorkspaceCard || root.Tag as string == "surface" ? Surface : root.Parent?.BackColor ?? Background;
        if(root.Tag as string=="divider") root.BackColor=Border;
        root.ForeColor = Text;
        WorkspaceChrome.ApplyControl(root, this);
        foreach (Control child in root.Controls) Apply(child);
        switch (root)
        {
            case Button button:
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Selection;
                button.Padding = button is NavigationButton ? new(52,8,12,8) : new(18, 10, 18, 10); button.MinimumSize = new(0, 44);
                if(button.Text is "Move up" or "Move down") { button.Padding=new(2); button.MinimumSize=new(36,36); }
                button.UseVisualStyleBackColor = false;
                if (button.Tag as string == "primary") { button.BackColor = Accent; button.ForeColor = Color.White; }
                if (button.Text.Contains("Close window")) button.FlatAppearance.MouseOverBackColor = Color.FromArgb(150,45,52);
                break;
            case LinkLabel link: link.LinkColor = Accent; link.ActiveLinkColor = Text; break;
            case Label label:
                label.BackColor = Color.Transparent;
                if (label.Tag as string == "muted") label.ForeColor = Muted;
                if (label.Tag as string == "accent") label.ForeColor = Accent;
                break;

            case ListView list:
                list.BackColor = Surface; list.BorderStyle = BorderStyle.None; list.GridLines = false;
                foreach (ListViewItem item in list.Items) { item.BackColor = Surface; item.ForeColor = Text; }
                if (!list.OwnerDraw)
                {
                    list.OwnerDraw = true;
                    // Native list resizing can preserve pixels from the previous column layout.
                    list.SizeChanged += (_,_) => list.Invalidate(true);
                    if(list.Tag as string!="instances")
                    {
                        var height=new ImageList { ImageSize=new(1,46) }; list.SmallImageList=height; list.Disposed+=(_,_)=>height.Dispose();
                        list.ClientSizeChanged+=(_,_)=>
                        {
                            if(list.Columns.Count==0) return;
                            int used=list.Columns.Cast<ColumnHeader>().Take(list.Columns.Count-1).Sum(column=>column.Width);
                            int width=Math.Max(120,list.ClientSize.Width-used-1);
                            if (list.Columns[^1].Width == width) return;
                            list.Columns[^1].Width=width;
                        };
                    }
                    list.DrawColumnHeader += (_,e) =>
                    {
                        using var fill = new SolidBrush(list.BackColor); e.Graphics.FillRectangle(fill,e.Bounds);
                        TextRenderer.DrawText(e.Graphics,e.Header!.Text,list.Font,Rectangle.Inflate(e.Bounds,-8,0),list.ForeColor,TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    };
                    list.DrawItem += (_,e) => { if (list.View != View.Details) e.DrawDefault = true; };
                    list.DrawSubItem += (_,e) =>
                    {
                        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
                        var paintState=e.Graphics.Save();
                        try
                        {
                        e.Graphics.SetClip(e.Bounds,System.Drawing.Drawing2D.CombineMode.Intersect);
                        var palette = For(list);
                        using var fill = new SolidBrush(e.Item!.Selected ? palette.Selection : list.BackColor); e.Graphics.FillRectangle(fill,e.Bounds);
                        string text=e.SubItem!.Text;
                        bool instances=list.Tag as string=="instances";
                        if(list.Tag as string=="instances" && e.ColumnIndex==5)
                        {
                            bool background=text.StartsWith("Background"); string state=background?"Background":text;
                            var bounds=new Rectangle(e.Bounds.X+12,e.Bounds.Y+(e.Bounds.Height-30)/2,Math.Min(e.Bounds.Width-20,background?116:84),30);
                            using var shape=WorkspaceCard.Rounded(bounds,14); using var badgeFill=new SolidBrush(palette.Selection); using var badgeBorder=new Pen(palette.Border);
                            e.Graphics.FillPath(badgeFill,shape); e.Graphics.DrawPath(badgeBorder,shape);
                            TextRenderer.DrawText(e.Graphics,state,list.Font,bounds,background?palette.Muted:palette.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                        }
                        else
                        {
                            var textBounds=Rectangle.Inflate(e.Bounds,-16,0);
                            if(instances && e.ColumnIndex==0)
                            {
                                int size=list.LogicalToDeviceUnits(28), inset=list.LogicalToDeviceUnits(22);
                                WorkspaceGlyph.Draw(e.Graphics,new(e.Bounds.Left+inset,e.Bounds.Top+(e.Bounds.Height-size)/2,size,size),"Roblox",palette.Text);
                                textBounds.X=e.Bounds.Left+inset+size+list.LogicalToDeviceUnits(18);
                                textBounds.Width=Math.Max(0,e.Bounds.Right-textBounds.X-inset);
                            }
                            if(instances && e.ColumnIndex==3 && double.TryParse(text,out double cpuValue)) text+="%";
                            var foreground = list.Tag as string == "activity" && e.ColumnIndex == 2 ? e.SubItem.ForeColor : list.ForeColor;
                            TextRenderer.DrawText(e.Graphics,text,list.Font,textBounds,foreground,TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        }
                        using var separator=new Pen(palette.Border); e.Graphics.DrawLine(separator,e.Bounds.Left,e.Bounds.Bottom-1,e.Bounds.Right,e.Bounds.Bottom-1);
                        if(e.Item.Selected && e.ColumnIndex==0) { using var marker=new Pen(palette.Accent,3); e.Graphics.DrawLine(marker,e.Bounds.Left+2,e.Bounds.Top,e.Bounds.Left+2,e.Bounds.Bottom); }
                        }
                        finally { e.Graphics.Restore(paintState); }
                    };
                }
                break;
            case TabControl tabs:
                if (tabs.DrawMode != TabDrawMode.OwnerDrawFixed)
                {
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.ItemSize = new(145,38); tabs.SizeMode = TabSizeMode.Fixed;
                    tabs.DrawItem += (_,e) =>
                    {
                        var palette = For(tabs);
                        using var fill = new SolidBrush(tabs.SelectedIndex == e.Index ? palette.Selection : palette.Surface); e.Graphics.FillRectangle(fill,e.Bounds);
                        TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,tabs.Font,e.Bounds,palette.Text,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    };
                }
                break;
            case ComboBox combo:
                combo.FlatStyle=FlatStyle.Flat;
                if(combo.DrawMode==DrawMode.Normal)
                {
                    combo.DrawMode=DrawMode.OwnerDrawFixed; combo.ItemHeight=Math.Max(28,combo.Font.Height+10);
                    combo.DrawItem+=(_,e)=>
                    {
                        var palette=For(combo);
                        using var fill=new SolidBrush((e.State&DrawItemState.Selected)!=0?palette.Selection:palette.Surface); e.Graphics.FillRectangle(fill,e.Bounds);
                        string value=e.Index>=0?combo.GetItemText(combo.Items[e.Index]) ?? "":combo.Text;
                        TextRenderer.DrawText(e.Graphics,value,combo.Font,Rectangle.Inflate(e.Bounds,-8,0),palette.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                        if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
                    };
                }
                break;
            case TextBox box: box.BorderStyle = box.Tag as string=="borderless" ? BorderStyle.None : BorderStyle.FixedSingle; break;
            case DataGridView grid:
                grid.BackgroundColor = Surface; grid.BorderStyle = BorderStyle.None; grid.GridColor = Border;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Background; grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
                grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Selection; grid.DefaultCellStyle.SelectionForeColor = Text;
                grid.RowTemplate.Height = 38; break;
        }
    }
}
