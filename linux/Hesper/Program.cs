using System.Text.Json.Nodes;

namespace Hesper;

static class Program
{
    static int Main(string[] args)
    {
        Native.Init();
        if (args.Length >= 1 && args[0] is "--version" or "-v") { Console.WriteLine($"Hesper {Setup.AppVersion}"); return 0; }
        // A test without windows: record N seconds into a folder, then (unless --no-process) make the notes
        if (args.Length >= 2 && args[0] == "--record" && int.TryParse(args[1], out var seconds))
            return RecordTest(seconds, args.Skip(2).FirstOrDefault(a => !a.StartsWith("--")) ?? Paths.DefaultNotesFolder(),
                              process: !args.Contains("--no-process")).GetAwaiter().GetResult();
        if (args.Length >= 1 && args[0] == "--devices") return Devices();
        if (args.Length >= 2 && args[0] == "--write-icon") { Brand.WriteIcons(args[1], again: true); return 0; }  // the AppImage's icon

        if (Native.MissingDesktop() is { } missing)
        {
            var text = $"Hesper needs {missing}. Install it with your distribution's software manager, then open Hesper again.";
            Console.Error.WriteLine(text);
            if (Hardware.OnPath("notify-send") != null) Platform.Run("notify-send", "Hesper", text);
            return 1;
        }
        Native.g_set_prgname("hesper");  // the windows' app id: matches hesper.desktop (name and icon in the dock)
        Native.g_set_application_name("Hesper");
        if (Native.gtk_init_check(IntPtr.Zero, IntPtr.Zero) == 0)
        {
            Console.Error.WriteLine("Hesper needs a desktop session (no display found).");
            return 1;
        }
        MainLoop.Install();
        FollowDarkMode();
        if (args.Length >= 2 && args[0] == "--capture") return Run(() => Capture(args[1], args.Length > 2 ? args[2] : ""));  // PNGs of every screen, light and dark
        if (args.Length >= 1 && args[0] == "--notify") return NotifyTest();

        // One Hesper per data folder: a test setup (HESPER_HOME) runs beside the real one
        var instance = Environment.GetEnvironmentVariable("HESPER_HOME") == null ? "" : "-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Paths.Home)))[..12];
        using var single = new Mutex(true, @"Local\Hesper" + instance, out var first);
        // After an update the new Hesper starts while the old one quits: wait for it
        if (!first && args.Contains("--after-update"))
            try { first = single.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { first = true; }  // the old one quit without letting go: ours now
        // hesper://… (the desktop starts Hesper with the link): to the running Hesper, or handled once started
        var link = args.FirstOrDefault(a => a.StartsWith("hesper:", StringComparison.OrdinalIgnoreCase));
        if (!first)
        {
            LinkPipe.Send(instance, link ?? "hesper://open");  // opened again from the menu: show it
            return 0;
        }
        Identity.Register();
        Updater.CleanUp();
        // Never vanish without a word: say what went wrong, and keep it in hesper.log
        MainLoop.Failed = e => Fail(e, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail((Exception)e.ExceptionObject, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) => { Paths.WriteLog("unobserved: " + e.Exception); e.SetObserved(); };
        try
        {
            _ = new TrayApp(instance, link, autostart: args.Contains("--autostart"));
            Native.gtk_main();
            return 0;
        }
        catch (Exception e) { Fail(e, fatal: true); return 1; }
    }

    static void Fail(Exception e, bool fatal)
    {
        Paths.WriteLog((fatal ? "crash: " : "error: ") + e);
        try { Message($"{(fatal ? "Hesper stopped" : "Something went wrong")}: {e.Message}\n\nDetails are in {Paths.Log}.", 3 /* GTK_MESSAGE_ERROR */, 1 /* OK */); }
        catch (Exception) { Console.Error.WriteLine(e); }
    }

    /// A question with Yes / No (quitting while recording).
    public static bool AskYesNo(string text) => Message(text, 1 /* GTK_MESSAGE_WARNING */, 4 /* GTK_BUTTONS_YES_NO */) == Native.GtkResponseYes;

    static int Message(string text, int type, int buttons)
    {
        var dialog = Native.gtk_message_dialog_new(IntPtr.Zero, 1 /* modal */, type, buttons, "%s", text);
        Native.gtk_window_set_title(dialog, "Hesper");
        var answer = Native.gtk_dialog_run(dialog);
        Native.gtk_widget_destroy(dialog);
        return answer;
    }

    /// GTK 3 apps don't follow the desktop's dark style by themselves (GNOME says it through its
    /// portal): the pages' light or dark look (prefers-color-scheme) follows it here.
    static void FollowDarkMode()
    {
        var scheme = DBus.Call(false, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop", "org.freedesktop.portal.Settings", "Read",
                               "('org.freedesktop.appearance', 'color-scheme')");
        var dark = scheme == "1" || (Environment.GetEnvironmentVariable("GTK_THEME") ?? "").Contains("dark", StringComparison.OrdinalIgnoreCase);
        if (Environment.GetEnvironmentVariable("HESPER_THEME") is { } forced) dark = forced == "dark";
        SetDark(dark);
    }

    static void SetDark(bool dark) => Native.g_object_set(Native.gtk_settings_get_default(), "gtk-application-prefer-dark-theme", dark ? 1 : 0, IntPtr.Zero);

    /// Runs a task on GTK's loop until it ends.
    static int Run(Func<Task> work)
    {
        var code = 0;
        MainLoop.Instance.Post(async _ =>
        {
            try { await work(); }
            catch (Exception e) { Console.Error.WriteLine(e); code = 1; }
            Native.gtk_main_quit();
        }, null);
        Native.gtk_main();
        return code;
    }

    /// Shows one notification the way the app does, and says which server shows them (support).
    static int NotifyTest()
    {
        Identity.Register();
        Console.WriteLine($"notification server: {Notifier.Server()}");
        Notifier.Show("Hesper", "Notifications work.", null);
        return 0;
    }

    /// lang: the app's language for the pictures ("fr"…), else the system's.
    static async Task Capture(string dir, string lang)
    {
        Directory.CreateDirectory(dir);
        Brand.WriteIcons(Path.Combine(Paths.Home, "icons"));
        var model = new AppModel();
        if (lang.Length > 0) model.Settings.AppLanguage = lang;  // for these pictures only: not saved
        var latest = Meetings.List(model.Settings.NotesFolder).FirstOrDefault(m => m!["hasNotes"]!.GetValue<bool>())?["id"]?.GetValue<string>();
        (string Page, int W, int H)[] pages =
        [
            ("home/detached", 400, 600), ("welcome", 640, 600),
            .. latest != null ? new[] { ("meeting/" + latest, 760, 760) } : [],
            .. new[] { "general", "recording", "notes", "models", "notifications", "privacy", "voices", "about" }.Select(p => ("settings/" + p, 780, 580)),
        ];
        var failed = 0;
        foreach (var dark in new[] { false, true })
        {
            SetDark(dark);
            foreach (var (page, w, h) in pages)
            {
                var win = new WebWindow(model, page, w, h);
                win.Show();
                for (var i = 0; i < 100 && !win.Loaded; i++) await Task.Delay(100);
                await Task.Delay(1500);  // the page asks for its state, then draws
                var name = page.Replace("/detached", "").Replace('/', '-') + (lang.Length > 0 ? "-" + lang : "") + (dark ? "-dark" : "-light") + ".png";
                var ok = win.Loaded && await win.CapturePng(Path.Combine(dir, name));
                Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}");
                if (!ok) failed++;
                win.Close();
            }
        }
        if (failed > 0) throw new InvalidOperationException($"{failed} page(s) couldn't be captured");
    }

    /// The sound devices, and how loud the default microphone and output are over 3 s (support).
    static int Devices()
    {
        Console.WriteLine($"pactl: {(Pulse.HasPactl ? "yes" : "no (install pulseaudio-utils to choose devices and notice calls)")}");
        foreach (var d in Pulse.Sources()) Console.WriteLine($"microphone: {d.Description}  [{d.Name}]");
        foreach (var d in Pulse.Sinks()) Console.WriteLine($"output: {d.Description}  [{d.Name}]");
        var folder = Path.Combine(Path.GetTempPath(), "hesper-devices-" + Environment.ProcessId);
        try
        {
            using var rec = new Recorder(folder);
            rec.Start();
            foreach (var n in rec.Notices) Console.WriteLine(n);
            float mic = 0, system = 0;
            for (var i = 0; i < 30; i++) { Thread.Sleep(100); mic = Math.Max(mic, rec.MicLevel ?? 0); system = Math.Max(system, rec.SystemLevel ?? 0); }
            rec.Stop();
            Console.WriteLine($"loudest over 3 s: microphone {mic:F3}, the computer's sound {system:F3}");
        }
        catch (Exception e) { Console.WriteLine($"can't record: {e.Message}"); return 1; }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
        return 0;
    }

    static async Task<int> RecordTest(int seconds, string notesFolder, bool process)
    {
        var folder = Paths.NewSession(notesFolder, DateTime.Now);
        using var rec = new Recorder(folder);
        rec.Start();
        foreach (var n in rec.Notices) Console.WriteLine(n);
        Console.WriteLine($"recording {seconds}s into {folder}");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        rec.Stop();
        rec.Dispose();
        Console.WriteLine($"stopped at {rec.Elapsed.TotalSeconds:F1}s");
        foreach (var f in new[] { "mic.wav", "system.wav" }.Select(f => Path.Combine(folder, f)).Where(File.Exists))
            Console.WriteLine($"  {Path.GetFileName(f)}: {(new FileInfo(f).Length - 44) / 2.0 / Wav.Rate:F1} s");
        if (!process) return 0;
        Console.WriteLine("processing");
        var r = await EngineRunner.Hesper(["process", folder], line =>
        {
            var (step, progress) = EngineRunner.Parse(line);
            if (step != null) Console.WriteLine($"  {step}");
            else if (progress == null && line.Length > 0) Console.WriteLine($"  | {line}");
        });
        Console.WriteLine(r.Ok ? $"notes: {Path.Combine(folder, "notes.md")}" : $"failed:\n{r.ErrorTail}");
        return r.Ok ? 0 : 1;
    }
}
