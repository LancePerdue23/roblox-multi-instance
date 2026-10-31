using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
namespace RobloxLaunchHelper;

internal static class WorkspaceChrome
{
    private static readonly ConditionalWeakTable<Control, ControlTheme> controlThemes = new();
    [DllImport("uxtheme.dll", CharSet=CharSet.Unicode)]
    private static extern int SetWindowTheme(nint window,string? application,string? classes);
    // These optional Windows dark-mode entry points are absent on older Windows releases.
    [DllImport("uxtheme.dll", EntryPoint="#135")]
    private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint="#133")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AllowDarkModeForWindow(nint window,[MarshalAs(UnmanagedType.U1)] bool allow);
    internal static void ApplyControl(Control control,ThemePalette palette)
    {
        if (control is not (ScrollableControl or ListView or TextBoxBase or ComboBox or DataGridView or ScrollBar)) return;
        controlThemes.GetValue(control, c => new ControlTheme(c)).Apply(palette);
        // Attach after native theme setup, so the theme's window subclass cannot
        // sit above our painter during lazy page/handle creation.
        WorkspaceScrollbars.Apply(control);
    }
    private sealed class ControlTheme
    {
        private readonly Control control;
        private bool dark, highContrast, applied;
        internal ControlTheme(Control control)
        {
            this.control=control;
            control.HandleCreated+=(_,_)=>ApplyNative();
            control.HandleDestroyed+=(_,_)=>applied=false;
        }
        internal void Apply(ThemePalette palette)
        {
            bool nextHighContrast=SystemInformation.HighContrast;
            bool nextDark=!nextHighContrast && palette.Text.R>150;
            if (applied && dark==nextDark && highContrast==nextHighContrast) return;
            highContrast=nextHighContrast; dark=nextDark;
            if(control.IsHandleCreated) ApplyNative();
        }
        private void ApplyNative()
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10,0,18362))
            {
                try
                {
                    // Permit native dark scrollbars without buffering the whole window.
                    SetPreferredAppMode(1); // AllowDark; each control opts in below.
                    AllowDarkModeForWindow(control.Handle,dark);
                }
                catch (EntryPointNotFoundException) { }
            }
            // A native themed scrollbar can animate directly onto the window DC
            // after our paint has completed. Suppress that competing renderer on
            // surfaces whose complete frame/scrollbars we paint ourselves.
            bool customScrollbars=!highContrast && (control is ScrollBar || WorkspaceScrollbars.OwnsNonClientPaint(control));
            _=SetWindowTheme(control.Handle,customScrollbars?"":highContrast?null:dark?"DarkMode_Explorer":"Explorer",customScrollbars?"":null);
            applied=true;
        }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    internal static void Apply(Form window,ThemePalette palette)
    {
        if(!window.IsHandleCreated)return;
        int dark=!SystemInformation.HighContrast && palette.Text.R>150?1:0;
        _=DwmSetWindowAttribute(window.Handle,20,ref dark,sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))
        {
            // Keep native caption buttons, dragging and snap behavior while matching the app.
            int caption = SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(palette.Background);
            int text = SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(palette.Text);
            int border = SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(palette.Border);
            _=DwmSetWindowAttribute(window.Handle,35,ref caption,sizeof(int));
            _=DwmSetWindowAttribute(window.Handle,36,ref text,sizeof(int));
            _=DwmSetWindowAttribute(window.Handle,34,ref border,sizeof(int));
        }
    }
}
