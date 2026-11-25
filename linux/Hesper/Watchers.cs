using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hesper;

/// Notices when a call starts and ends: a meeting app (Teams, Zoom, a browser…) is recording from a
/// microphone (the sound server says which program). Hesper's own recording doesn't count. Needs no
/// permission and never touches the audio. As the Windows app's CallMonitor.
sealed class CallMonitor : IDisposable
{
    /// Program names whose recording means "a call": the Mac's and Windows' lists, as Linux names them.
    /// Not Discord: its voice chat is mostly games.
    static readonly (string Binary, string Name)[] MeetingApps =
    [
        ("teams-for-linux", "Microsoft Teams"), ("teams", "Microsoft Teams"), ("zoom", "Zoom"), ("webex", "Webex"), ("slack", "Slack"),
        ("google-chrome", "Google Chrome"), ("chrome", "Google Chrome"), ("chromium", "Chromium"), ("msedge", "Microsoft Edge"),
        ("microsoft-edge", "Microsoft Edge"), ("firefox", "Firefox"), ("librewolf", "LibreWolf"), ("brave", "Brave"), ("opera", "Opera"),
        ("vivaldi", "Vivaldi"), ("epiphany", "GNOME Web"),
    ];

    public event Action<string>? CallStarted;
    public event Action? CallEnded;
    readonly UiTimer timer = new() { Interval = 3000 };
    DateTime? busySince, idleSince;
    bool inCall, checking;

    public CallMonitor()
    {
        if (!Pulse.HasPactl) return;  // nothing to ask: no call is noticed
        timer.Tick += async (_, _) =>
        {
            if (checking) return;
            checking = true;
            var app = await Task.Run(CallApp);  // pactl takes a moment: not on the UI thread
            checking = false;
            Tick(app);
        };
        timer.Start();
    }

    void Tick(string? app)
    {
        if (app != null)
        {
            idleSince = null;
            busySince ??= DateTime.Now;
            if (!inCall && DateTime.Now - busySince >= TimeSpan.FromSeconds(4))
            {
                inCall = true;
                CallStarted?.Invoke(app);
            }
        }
        else
        {
            busySince = null;
            if (!inCall) return;
            idleSince ??= DateTime.Now;
            if (DateTime.Now - idleSince >= TimeSpan.FromSeconds(45))  // a short drop isn't the end of the call
            {
                inCall = false;
                CallEnded?.Invoke();
            }
        }
    }

    /// The meeting app recording right now, or null.
    static string? CallApp()
    {
        var me = Environment.ProcessId;
        foreach (var (pid, binary) in Pulse.Recording())
        {
            if (pid == me) continue;
            var name = binary.ToLowerInvariant();
            foreach (var (b, display) in MeetingApps)
                if (name == b || (b.Length > 3 && name.StartsWith(b))) return display;
        }
        return null;
    }

    public void Dispose() => timer.Dispose();
}

/// A shortcut for the whole desktop ("Ctrl+Alt+R"). Only an X11 session lets an app take one; on
/// Wayland the desktop's own keyboard settings can run `xdg-open hesper://toggle` instead (Settings
/// says so).
sealed unsafe class Hotkey : IDisposable
{
    public event Action? Pressed;
    static readonly bool x11 = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") != "wayland"
                               && Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 } && Native.Has(Native.X11Lib);
    public static bool Supported => x11;

    IntPtr display;
    ulong root;
    (int Code, uint Mods)? grabbed;
    Thread? thread;
    volatile bool stopping;
    readonly object gate = new();
    static int lastError;

    // NumLock (Mod2) and CapsLock (Lock) mustn't stop the shortcut: grabbed with each combination
    static readonly uint[] Ignored = [0, 2, 16, 18];

    /// Registers the shortcut; false when it's not valid, not possible here, or another app has it.
    public bool Set(string shortcut)
    {
        lock (gate)
        {
            if (!x11) return shortcut.Length == 0;
            Open();
            if (display == IntPtr.Zero) return false;
            if (grabbed is var (code0, mods0))
                foreach (var i in Ignored) Native.XUngrabKey(display, code0, mods0 | i, root);
            grabbed = null;
            if (Parse(shortcut) is not var (mods, keysym)) { Native.XSync(display, 0); return shortcut.Length == 0; }
            var code = Native.XKeysymToKeycode(display, keysym);
            if (code == 0) return false;
            lastError = 0;
            foreach (var i in Ignored) Native.XGrabKey(display, code, mods | i, root, 0, 1 /* GrabModeAsync */, 1);
            Native.XSync(display, 0);
            if (lastError != 0)  // BadAccess: another app has it
            {
                foreach (var i in Ignored) Native.XUngrabKey(display, code, mods | i, root);
                Native.XSync(display, 0);
                return false;
            }
            grabbed = (code, mods);
            return true;
        }
    }

    void Open()
    {
        if (display != IntPtr.Zero) return;
        Native.XSetErrorHandler((IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)&OnError);  // else Xlib's default one exits
        display = Native.XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) return;
        root = Native.XDefaultRootWindow(display);
        thread = new Thread(Listen) { IsBackground = true, Name = "shortcut" };
        thread.Start();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static int OnError(IntPtr display, IntPtr xErrorEvent)
    {
        lastError = Marshal.ReadByte(xErrorEvent, 32);  // XErrorEvent.error_code (after type, display, resourceid, serial)
        return 0;
    }

    void Listen()
    {
        var ev = stackalloc byte[192];  // XEvent
        while (!stopping)
        {
            var pressed = false;
            lock (gate)
                while (display != IntPtr.Zero && Native.XPending(display) > 0)
                {
                    Native.XNextEvent(display, ev);
                    if (*(int*)ev == 2 /* KeyPress */ && grabbed is var (code, _) && *(uint*)(ev + 84) == code) pressed = true;
                }
            if (pressed) MainLoop.Instance.Post(_ => Pressed?.Invoke(), null);
            Thread.Sleep(40);
        }
    }

    /// "Ctrl+Alt+R" → (ControlMask | Mod1Mask, the R key); at least one of Ctrl, Alt, Super.
    static (uint, ulong)? Parse(string s)
    {
        uint mods = 0;
        ulong keysym = 0;
        foreach (var part in s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= 4; break;
                case "alt": mods |= 8; break;
                case "shift": mods |= 1; break;
                case "win" or "super" or "meta": mods |= 64; break;
                default:
                    var name = part.Length == 1 ? part.ToLowerInvariant() : part switch
                    {
                        "Space" => "space", "Enter" => "Return", "Escape" => "Escape", "Tab" => "Tab", "Backspace" => "BackSpace",
                        "ArrowUp" => "Up", "ArrowDown" => "Down", "ArrowLeft" => "Left", "ArrowRight" => "Right",
                        "PageUp" => "Prior", "PageDown" => "Next", "Home" => "Home", "End" => "End", "Insert" => "Insert", "Delete" => "Delete",
                        _ => part,  // F1…F12
                    };
                    keysym = Native.XStringToKeysym(name);
                    if (keysym == 0) return null;
                    break;
            }
        }
        return keysym != 0 && (mods & (4 | 8 | 64)) != 0 ? (mods, keysym) : null;
    }

    public void Dispose()
    {
        stopping = true;
        lock (gate)
        {
            if (display == IntPtr.Zero) return;
            if (grabbed is var (code, mods))
                foreach (var i in Ignored) Native.XUngrabKey(display, code, mods | i, root);
            Native.XCloseDisplay(display);
            display = IntPtr.Zero;
        }
    }
}

/// Whether nobody is using the computer right now, by the user's rules (Settings → Notes → When to
/// write the notes): set hours, no keyboard or mouse for a while, or the screen locked — and always on
/// power. As MacUse on the Mac and PcUse on Windows. Asked of GNOME (Mutter), logind or X11.
static class PcUse
{
    public static double IdleMinutes
    {
        get
        {
            // GNOME (Wayland or X11): Mutter's idle monitor
            if (DBus.Call(false, "org.gnome.Mutter.IdleMonitor", "/org/gnome/Mutter/IdleMonitor/Core", "org.gnome.Mutter.IdleMonitor",
                          "GetIdletime") is { } ms && ulong.TryParse(ms, out var idle))
                return idle / 60000.0;
            return X11Idle() ?? 0;
        }
    }

    static unsafe double? X11Idle()
    {
        if (Environment.GetEnvironmentVariable("DISPLAY") is not { Length: > 0 } || !Native.Has(Native.XssLib)) return null;
        var d = Native.XOpenDisplay(IntPtr.Zero);
        if (d == IntPtr.Zero) return null;
        try
        {
            var info = Native.XScreenSaverAllocInfo();
            if (info == IntPtr.Zero || Native.XScreenSaverQueryInfo(d, Native.XDefaultRootWindow(d), info) == 0) return null;
            var idle = (ulong)Marshal.ReadInt64(info, 24);  // XScreenSaverInfo.idle (ms), after window, state, kind, til_or_since
            Native.XFree(info);
            return idle / 60000.0;
        }
        finally { Native.XCloseDisplay(d); }
    }

    /// The screen is locked: logind says so for this session (GNOME, KDE…), else GNOME's screensaver.
    static bool Locked =>
        DBus.Call(true, "org.freedesktop.login1", "/org/freedesktop/login1/session/auto", "org.freedesktop.DBus.Properties", "Get",
                  "('org.freedesktop.login1.Session', 'LockedHint')") is { } hint
            ? hint == "true"
            : DBus.Call(false, "org.gnome.ScreenSaver", "/org/gnome/ScreenSaver", "org.gnome.ScreenSaver", "GetActive") == "true";

    /// On mains power (a desktop without any power supply listed is too).
    public static bool OnPower
    {
        get
        {
            const string supplies = "/sys/class/power_supply";
            if (!Directory.Exists(supplies)) return true;
            var mains = Directory.GetDirectories(supplies).Where(d => Read(Path.Combine(d, "type")) == "Mains").ToList();
            if (mains.Count == 0) return !Directory.GetDirectories(supplies).Any(d => Read(Path.Combine(d, "type")) == "Battery");
            return mains.Any(d => Read(Path.Combine(d, "online")) == "1");
        }
    }

    static string Read(string file)
    {
        try { return File.ReadAllText(file).Trim(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    public static bool Free(AppSettings s)
    {
        if (!OnPower) return false;
        var minute = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
        bool InWindow(int[] w) => w[0] <= w[1] ? minute >= w[0] && minute < w[1] : minute >= w[0] || minute < w[1];
        return (s.LaterInWindows && s.LaterWindows.Any(InWindow))
            || (s.LaterWhenIdle && IdleMinutes >= s.LaterIdleMinutes)
            || (s.LaterWhenLocked && Locked);
    }

    /// Someone came back (to stop between two meetings' notes).
    public static bool InUse => !Locked && IdleMinutes < 1;
}
