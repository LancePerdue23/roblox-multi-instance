using System.Text.Json;
using System.Windows;
using RobloxMultiInstance.Desktop;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        var window=new Window();
        using(var owner=new ShortcutOwner(window))
        {
            // F10/F11 with three modifiers minimize interference with user bindings.
            var keys=JsonSerializer.SerializeToElement(new { Manager=0x70079, Saved=0x7007a });
            for(int cycle=0;cycle<12;cycle++)
            {
                var failures=owner.Enable(keys);
                if(args.Contains("--require-registration"))Check(owner.Registered==2,"Both fixture hotkeys register on the interactive desktop");
                Check(owner.Registered+failures.Length==2,"Every binding has an outcome");
                Check(owner.Enabled==(owner.Registered>0),"Enabled requires a native registration");
                Check(owner.Status==(owner.Registered==2?"enabled":owner.Registered==1?"partial":"failed"),"Accurate state");
                foreach(var failure in failures)Check(failure.Contains("Windows error"),"Native reason retained");
                owner.Capture(true);Check(!owner.Enabled&&owner.Status=="capturing","Capture releases own shortcuts");
                owner.Capture(false);Check(owner.Registered+owner.Conflicts.Length==2,"Capture restores registrations");
                owner.Disable();Check(!owner.Requested&&!owner.Enabled&&owner.Status=="disabled","Disable clears ownership");
            }
            // Same combination twice deterministically exercises a registration failure.
            owner.Enable(JsonSerializer.SerializeToElement(new { Manager=0x70079, Saved=0x70079 }));
            Check(owner.Conflicts.Length>0,"Duplicate native registration reports failure");
            owner.Disable();owner.Disable();
            using var acquired = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var competing = Task.Run(() =>
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                string name = @"Local\RobloxLaunchHelper-WindowShortcuts-" + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value + "-" + process.SessionId;
                using var mutex = new Mutex(false, name);
                mutex.WaitOne(); acquired.Set(); release.Wait(); mutex.ReleaseMutex();
            });
            try
            {
                Check(acquired.Wait(TimeSpan.FromSeconds(5)),"Competing owner started");
                owner.Enable(keys);
                Check(!owner.Enabled && owner.Status=="failed" && owner.Conflicts.Any(c=>c.Contains("Another app instance")),"Competing owner is reported without registering shortcuts");
            }
            finally { release.Set(); competing.GetAwaiter().GetResult(); }
            owner.Enable(keys);
            if(args.Contains("--require-registration"))Check(owner.Registered==2,"Ownership is available after competitor exits");
        }
        window.Close();
        Console.WriteLine($"Passed {checks} native shortcut lifecycle checks.");
    }
}