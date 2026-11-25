using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Hesper;

/// What the shared code (AppModel, Common.cs…) asks of the system, the Linux way. The Windows app
/// (win/Hesper/Platform.cs) has the same members.
static class Platform
{
    public const string Name = "linux";

    /// The update feeds: latest-linux.json and latest-linux-beta.json.
    public const string Feed = "latest-linux";

    public static bool Wayland => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland"
                                  || (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }
                                      && Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") != "x11");

    /// What this desktop allows, for the pages: a tray icon or not, a window kept on top (not on
    /// Wayland), global shortcuts (X11 only), calls noticed (pactl).
    public static JsonObject? Desktop() => new()
    {
        ["tray"] = TrayApp.HasTray, ["wayland"] = Wayland, ["onTop"] = !Wayland, ["hotkeys"] = Hotkey.Supported, ["calls"] = Pulse.HasPactl,
    };

    public static void CopyText(string text)
    {
        var clipboard = Native.gtk_clipboard_get(Native.gdk_atom_intern("CLIPBOARD", 0));
        Native.gtk_clipboard_set_text(clipboard, text, -1);
        Native.gtk_clipboard_store(clipboard);  // still there once Hesper quits
    }

    public static string? ChooseFolder(string title) => Choose(title, 2 /* GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER */, null);

    /// A file of one of these types (extensions); anyFile: "All files" offered too.
    public static string? ChooseFile(string title, string kind, string[] types, bool anyFile) =>
        Choose(title, 0 /* GTK_FILE_CHOOSER_ACTION_OPEN */, chooser =>
        {
            var filter = Native.gtk_file_filter_new();
            Native.gtk_file_filter_set_name(filter, kind);
            foreach (var t in types)
            {
                Native.gtk_file_filter_add_pattern(filter, "*." + t);
                Native.gtk_file_filter_add_pattern(filter, "*." + t.ToUpperInvariant());
            }
            Native.gtk_file_chooser_add_filter(chooser, filter);
            if (!anyFile) return;
            var all = Native.gtk_file_filter_new();
            Native.gtk_file_filter_set_name(all, "*");
            Native.gtk_file_filter_add_pattern(all, "*");
            Native.gtk_file_chooser_add_filter(chooser, all);
        });

    /// GTK's file chooser, native (the desktop's own dialog, through its portal, when it has one).
    static string? Choose(string title, int action, Action<IntPtr>? filters)
    {
        var dialog = Native.gtk_file_chooser_native_new(title, IntPtr.Zero, action, null, null);
        try
        {
            filters?.Invoke(dialog);
            return Native.gtk_native_dialog_run(dialog) == Native.GtkResponseAccept ? Native.TakeStr(Native.gtk_file_chooser_get_filename(dialog)) : null;
        }
        finally { Native.g_object_unref(dialog); }
    }

    public static void ShowFolder(string folder) => Run("xdg-open", folder);

    /// The file's folder, with the file selected (by the file manager, when it says it can).
    public static void ShowFile(string file)
    {
        if (DBus.Call(false, "org.freedesktop.FileManager1", "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1", "ShowItems",
                      $"([{DBus.Quote(new Uri(file).AbsoluteUri)}], '')") == null)
            ShowFolder(Path.GetDirectoryName(file)!);
    }

    /// To the Trash: `gio trash` (any drive), else the home Trash by hand (the freedesktop.org spec).
    public static void Trash(string path)
    {
        if (Hardware.OnPath("gio") != null)
        {
            using var p = Process.Start(new ProcessStartInfo("gio") { ArgumentList = { "trash", "--", path }, UseShellExecute = false, RedirectStandardError = true })!;
            p.WaitForExit(30_000);
            if (p.HasExited && p.ExitCode == 0) return;
        }
        var share = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var files = Path.Combine(share, "Trash", "files");
        var info = Path.Combine(share, "Trash", "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);
        var name = Path.GetFileName(path.TrimEnd('/'));
        for (var n = 2; Directory.Exists(Path.Combine(files, name)) || File.Exists(Path.Combine(files, name)) || File.Exists(Path.Combine(info, name + ".trashinfo")); n++)
            name = $"{Path.GetFileName(path.TrimEnd('/'))} {n}";
        var uri = string.Join("/", Path.GetFullPath(path).Split('/').Select(Uri.EscapeDataString));
        File.WriteAllText(Path.Combine(info, name + ".trashinfo"), $"[Trash Info]\nPath={uri}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
        var dest = Path.Combine(files, name);
        try
        {
            if (Directory.Exists(path)) Directory.Move(path, dest); else File.Move(path, dest);
        }
        catch (IOException)  // another drive: copied, then deleted
        {
            if (Directory.Exists(path)) { CopyDirectory(path, dest); Directory.Delete(path, true); }
            else { File.Copy(path, dest); File.Delete(path); }
        }
    }

    public static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// A moment marked: a short sound.
    public static void Ding() => Sound.Ding();

    public static void OpenNotificationSettings()
    {
        if (Hardware.OnPath("gnome-control-center") != null) Run("gnome-control-center", "notifications");
        else if (Hardware.OnPath("systemsettings") != null) Run("systemsettings", "kcm_notifications");
        else if (Hardware.OnPath("xfce4-notifyd-config") != null) Run("xfce4-notifyd-config");
    }

    static Process? inhibitor;

    /// on: the computer doesn't go to sleep (the screen may still turn off), through logind
    /// (systemd-inhibit holds the lock while its command runs: one that ends with Hesper, crash or not).
    public static void KeepAwake(bool on)
    {
        if (on == (inhibitor is { HasExited: false })) return;
        if (!on) { try { inhibitor?.Kill(); } catch (InvalidOperationException) { } inhibitor = null; return; }
        if (Hardware.OnPath("systemd-inhibit") == null) return;
        try
        {
            inhibitor = Process.Start(new ProcessStartInfo("systemd-inhibit")
            {
                ArgumentList = { "--what=sleep", "--who=Hesper", "--why=Recording or processing a meeting", "--mode=block",
                                 "tail", $"--pid={Environment.ProcessId}", "-f", "/dev/null" },
                UseShellExecute = false, RedirectStandardError = true,
            });
        }
        catch (System.ComponentModel.Win32Exception e) { Paths.WriteLog("keep awake: " + e.Message); }
    }

    /// Starts a program (no shell), without waiting for it.
    public static void Run(string program, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false };
            foreach (var a in args) info.ArgumentList.Add(a);
            Process.Start(info)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e) { Paths.WriteLog($"{program}: {e.Message}"); }
    }
}

/// Where Hesper starts from: its AppImage (which the AppImage runtime names in $APPIMAGE), else this
/// program (a build run from its folder).
static class Launcher
{
    public static string? Path => Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } a && File.Exists(a) ? a : Environment.ProcessPath;

    /// The Exec= line of a .desktop file for it (quoted per the Desktop Entry spec).
    public static string Exec(string args)
    {
        var q = new StringBuilder("\"");
        foreach (var c in (Path ?? "hesper").Replace("%", "%%"))
        {
            if (c is '"' or '`' or '$' or '\\') q.Append('\\');
            q.Append(c);
        }
        q.Append('"');
        return q.ToString().Replace("\\", "\\\\") + (args.Length > 0 ? " " + args : "");
    }

    public static string ConfigHome => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    public static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
}

/// "Open Hesper at login": a file in ~/.config/autostart (no admin rights), as the Windows Run key.
static class Startup
{
    static string File => Path.Combine(Launcher.ConfigHome, "autostart", "hesper.desktop");

    public static bool Enabled
    {
        get
        {
            try { return System.IO.File.Exists(File) && !System.IO.File.ReadAllText(File).Contains("Hidden=true"); }
            catch (IOException) { return false; }
        }
        set
        {
            if (!value) { try { System.IO.File.Delete(File); } catch (IOException) { } return; }
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, Content);
        }
    }

    static string Content => $"""
        [Desktop Entry]
        Type=Application
        Name=Hesper
        Comment=Private meeting notes, fully offline
        Exec={Launcher.Exec("--autostart")}
        Icon={Identity.Icon}
        Terminal=false
        X-GNOME-Autostart-enabled=true

        """;

    /// The AppImage moved (or was updated under another name): the login item follows it.
    public static void Refresh()
    {
        if (Enabled && System.IO.File.ReadAllText(File) != Content) System.IO.File.WriteAllText(File, Content);
    }
}

/// Who Hesper is to the desktop: a menu entry (~/.local/share/applications/hesper.desktop, which also
/// gives its windows and notifications their name and icon) that opens hesper:// links, and its icons.
/// All per user, no admin rights. As Identity on Windows.
static class Identity
{
    public static string Icon => Path.Combine(Paths.Home, "icons", "hesper.png");
    static string Entry => Path.Combine(Launcher.DataHome, "applications", "hesper.desktop");

    public static void Register()
    {
        try
        {
            Brand.WriteIcons(Path.Combine(Paths.Home, "icons"));
            // A test setup (HESPER_HOME) mustn't take the real one's menu entry and links
            if (Environment.GetEnvironmentVariable("HESPER_HOME") != null || Launcher.Path == null) return;
            var content = $"""
                [Desktop Entry]
                Type=Application
                Name=Hesper
                GenericName=Meeting notes
                Comment=Private meeting notes, fully offline
                Exec={Launcher.Exec("%u")}
                Icon={Icon}
                Terminal=false
                Categories=Office;AudioVideo;Audio;
                Keywords=meeting;notes;transcript;recorder;minutes;
                MimeType=x-scheme-handler/hesper;
                StartupWMClass=Hesper

                """;
            if (!File.Exists(Entry) || File.ReadAllText(Entry) != content)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Entry)!);
                File.WriteAllText(Entry, content);
                // hesper:// links open with it
                if (Hardware.OnPath("xdg-mime") != null) Platform.Run("xdg-mime", "default", "hesper.desktop", "x-scheme-handler/hesper");
                if (Hardware.OnPath("update-desktop-database") != null) Platform.Run("update-desktop-database", "-q", Path.GetDirectoryName(Entry)!);
            }
            Startup.Refresh();
        }
        catch (Exception e)  // never a reason not to start
        {
            Paths.WriteLog("desktop entry: " + e.Message);
        }
    }

    /// Why Hesper's notifications might not be seen, as an English text the pages translate; null
    /// when the desktop shows them.
    public static string? NotificationProblem() =>
        DBus.HasOwner("org.freedesktop.Notifications") ? null : "No notification service is running on this desktop, so you won't see these.";

    /// Uninstalling: the menu entry (and so the links) go too.
    public static void Remove()
    {
        try { File.Delete(Entry); } catch (IOException) { }
    }
}

/// Recording one app's sound only (Windows 11's process loopback): not on Linux.
static class ProcessLoopbackCapture
{
    public static bool Supported => false;
}

/// Linux: no classic Outlook. The calendar is the calendar link's (Settings → Recording → Calendar),
/// which CalendarSource merges in; this side has nothing of its own to read.
sealed class OutlookCalendar(bool on) : CalendarSource, IDisposable
{
    public static bool Available => false;
    public static bool Installed => false;
    bool enabled = on;

    public override void SetEnabled(bool on) => enabled = on;
    public override void Refresh() { }
    public void Dispose() { }
}

/// Linux: no classic Outlook to write e-mails with (mailto, Gmail or Outlook on the web instead).
static partial class Share
{
    public static string? OutlookPath() => null;
}

/// Linux: no OneDrive app (a synced folder — Nextcloud, Dropbox… — is "Another folder…").
static partial class Storage
{
    public static List<(string Name, string Path)> OneDriveAccounts() => [];
}

/// Linux: the new AppImage is downloaded beside the running one, checked against its SHA-256,
/// swapped in and started.
static partial class Updater
{
    public static async Task Install(JsonObject info, Action<double> progress)
    {
        var url = info["url"]!.GetValue<string>();
        var sha = info["sha256"]!.GetValue<string>().ToLowerInvariant();
        if (!url.StartsWith("https://github.com/Gbally/Hesper/", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected download address.");
        var app = Environment.GetEnvironmentVariable("APPIMAGE");
        if (app is not { Length: > 0 } || !File.Exists(app))
            throw new InvalidOperationException("This Hesper wasn't started from its AppImage: download the new version from the website.");
        var tmp = app + ".new";
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
        using (var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            r.EnsureSuccessStatusCode();
            var size = r.Content.Headers.ContentLength ?? 0;
            await using var from = await r.Content.ReadAsStreamAsync();
            await using var to = File.Create(tmp);
            var buf = new byte[1 << 20];
            long got = 0;
            int n;
            while ((n = await from.ReadAsync(buf)) > 0) { await to.WriteAsync(buf.AsMemory(0, n)); got += n; if (size > 0) progress((double)got / size); }
        }
        string actual;
        await using (var f = File.OpenRead(tmp)) actual = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
        if (actual != sha) { File.Delete(tmp); throw new InvalidOperationException("The download isn't the expected file."); }
        File.SetUnixFileMode(tmp, File.GetUnixFileMode(app) | UnixFileMode.UserExecute);
        File.Move(tmp, app, overwrite: true);  // the running one stays mounted until it quits
        Process.Start(new ProcessStartInfo(app) { UseShellExecute = false, ArgumentList = { "--after-update" } });
    }

    /// A download an update left behind.
    public static void CleanUp()
    {
        try { if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } a) File.Delete(a + ".new"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// Linux: the login item and the menu entry go, the notes and the AppImage to the Trash, then Hesper's
/// folder once it has quit.
static partial class Uninstall
{
    public static void Run(string notesFolder, bool removeNotes)
    {
        Startup.Enabled = false;
        Identity.Remove();
        if (removeNotes && Directory.Exists(notesFolder)) Platform.Trash(notesFolder);
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } app && File.Exists(app))
            try { Platform.Trash(app); } catch (IOException e) { Paths.WriteLog("uninstall: " + e.Message); }
        Process.Start(new ProcessStartInfo("/bin/sh")
        {
            ArgumentList = { "-c", "sleep 3; rm -rf -- \"$1\"", "hesper-uninstall", Paths.Home }, UseShellExecute = false,
        });
    }
}

/// The desktop's notifications (libnotify); a click on one does what the app said (opens a meeting,
/// stops a recording…). Without libnotify, `notify-send` (no click).
static unsafe class Notifier
{
    static bool? ready;
    static readonly Dictionary<IntPtr, Action?> shown = [];

    public static void Show(string title, string text, Action? click)
    {
        ready ??= Native.Has(Native.NotifyLib) && Native.notify_init("Hesper") != 0;
        if (ready != true)
        {
            if (Hardware.OnPath("notify-send") != null) Platform.Run("notify-send", "--app-name=Hesper", $"--icon={Identity.Icon}", title, text);
            return;
        }
        var n = Native.notify_notification_new(title, text, File.Exists(Identity.Icon) ? Identity.Icon : null);
        Native.notify_notification_set_hint(n, "desktop-entry", Native.g_variant_new_string("hesper"));
        if (click != null)
            Native.notify_notification_add_action(n, "default", "Open", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&OnAction,
                                                  IntPtr.Zero, IntPtr.Zero);
        Native.Connect(n, "closed", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnClosed, IntPtr.Zero);
        shown[n] = click;
        if (Native.notify_notification_show(n, out var error) == 0)
        {
            Paths.WriteLog("notification: " + Native.ErrorText(error));
            shown.Remove(n);
            Native.g_object_unref(n);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnAction(IntPtr n, IntPtr action, IntPtr data)
    {
        if (shown.TryGetValue(n, out var click) && click != null) MainLoop.Instance.Post(_ => click(), null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnClosed(IntPtr n, IntPtr data)
    {
        if (shown.Remove(n)) Native.g_object_unref(n);
    }

    /// The notification server's name and version (Hesper --notify, for support).
    public static string Server()
    {
        if (!Native.Has(Native.NotifyLib) || Native.notify_init("Hesper") == 0) return "libnotify isn't installed";
        return Native.notify_get_server_info(out var name, out var vendor, out var version, out _) != 0
            ? $"{Native.TakeStr(name)} {Native.TakeStr(version)} ({Native.TakeStr(vendor)})" : "no notification server answers";
    }
}
