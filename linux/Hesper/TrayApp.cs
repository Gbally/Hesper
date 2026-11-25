using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hesper;

/// The app: an icon in the top bar (AppIndicator: GNOME with its AppIndicator extension, as Ubuntu
/// has it; KDE, Cinnamon, Xfce…) whose menu opens Hesper's window — the home view, the Windows
/// popover's page, in a normal window. With no tray on this desktop, that window is the app: it opens
/// at launch, and closing it only hides it (launching Hesper again shows it). Meetings and Settings
/// open in their own windows. As TrayApp on Windows.
sealed class TrayApp
{
    /// This desktop shows tray icons (something answers for them on D-Bus, and AppIndicator is installed).
    public static bool HasTray { get; private set; }

    readonly AppModel model = new();
    readonly IntPtr indicator, record, stop;
    readonly UiTimer live = new() { Interval = 200 }, coalesce = new() { Interval = 150 }, label = new() { Interval = 1000 };
    readonly Hotkey hotkey = new(), markKey = new();
    string markKeySet = "";
    WebWindow? home;
    bool quitting;
    GCHandle self;

    public unsafe TrayApp(string instance = "", string? link = null, bool autostart = false)
    {
        self = GCHandle.Alloc(this);
        HasTray = Environment.GetEnvironmentVariable("HESPER_NO_TRAY") != "1" && Native.Has(Native.IndicatorLib)
                  && DBus.HasOwner("org.kde.StatusNotifierWatcher");
        if (HasTray)
        {
            var menu = Native.gtk_menu_new();
            IntPtr Item(string text, Action onClick)
            {
                var item = Native.gtk_menu_item_new_with_label(T(text));
                actions[item] = onClick;
                Native.Connect(item, "activate", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnMenuItem, GCHandle.ToIntPtr(self));
                Native.gtk_menu_shell_append(menu, item);
                return item;
            }
            var openItem = Item("Open Hesper", OpenHome);
            record = Item("Record", () => model.StartRecording(""));
            stop = Item("Stop and create notes", () => model.StopRecording());
            Native.gtk_menu_shell_append(menu, Native.gtk_separator_menu_item_new());
            Item("Settings…", () => ShowSettings(""));
            Item("Quit Hesper", Quit);
            Native.gtk_widget_show_all(menu);
            Native.gtk_widget_set_visible(stop, 0);

            indicator = Native.app_indicator_new("hesper", "hesper-tray", 0 /* APP_INDICATOR_CATEGORY_APPLICATION_STATUS */);
            Native.app_indicator_set_icon_theme_path(indicator, Path.Combine(Paths.Home, "icons"));
            Native.app_indicator_set_icon_full(indicator, "hesper-tray", "Hesper");
            Native.app_indicator_set_title(indicator, "Hesper");
            Native.app_indicator_set_menu(indicator, menu);
            Native.app_indicator_set_secondary_activate_target(indicator, openItem);  // a middle click opens Hesper
            Native.app_indicator_set_status(indicator, 1 /* APP_INDICATOR_STATUS_ACTIVE */);
        }
        Paths.WriteLog($"Linux desktop: tray {HasTray}, {(Platform.Wayland ? "Wayland" : "X11")}, shortcuts {Hotkey.Supported}, calls noticed {Pulse.HasPactl}");

        model.Changed += () => { if (!coalesce.Enabled) coalesce.Start(); };  // progress lines come fast: at most ~6 redraws a second
        coalesce.Tick += (_, _) => { coalesce.Stop(); OnChanged(); };
        model.Notify += (title, text, action) => Notifier.Show(title, text, action);
        hotkey.Pressed += model.ToggleRecording;
        if (!hotkey.Set(model.Settings.Hotkey) && model.Settings.Hotkey.Length > 0 && Hotkey.Supported)
            Paths.WriteLog($"the shortcut {model.Settings.Hotkey} couldn't be registered (another app has it?)");
        model.HotkeyChanged += () => hotkey.Set(model.Settings.Hotkey);
        markKey.Pressed += model.MarkMoment;
        model.OpenMeetingWindow += ShowMeeting;
        model.OpenSettingsWindow += ShowSettings;
        model.OpenWelcomeWindow += ShowWelcome;
        model.QuitRequested += Quit;
        model.ShowView += ShowView;
        model.DetachRequested += OpenHome;  // the home view is always a window here
        model.KeepOnTopChanged += on => home?.SetKeepAbove(on);
        live.Tick += (_, _) => WebWindow.Broadcast("live", model.Live);
        label.Tick += (_, _) => UpdateLabel();

        // hesper:// links: opened while Hesper runs (another Hesper hands them over), or the one it was started with
        LinkPipe.Listen(instance, url => MainLoop.Instance.Post(_ => model.HandleLink(url), null));
        if (link != null)
        {
            var once = new UiTimer { Interval = 1500 };
            once.Tick += (_, _) => { once.Dispose(); model.HandleLink(link); };
            once.Start();
        }

        model.Settings.WriteConfig();
        // An app that isn't there after a restart notices no call and reminds of nothing: on by default
        // from the first launch, and on the welcome window's last page to turn off
        if (model.Settings.IsNew && Environment.GetEnvironmentVariable("HESPER_HOME") == null) Startup.Enabled = true;
        var welcome = false;
        if (!model.Ready && model.Settings.Welcomed && Setup.WasInstalled)
        {
            // An update (or a new engine): set up again in the background; the home window shows it
            var later = new UiTimer { Interval = 300 };
            later.Tick += (_, _) => { later.Dispose(); _ = model.RunSetup(); };
            later.Start();
        }
        else if (!model.Ready || !model.Settings.Welcomed) { ShowWelcome(); welcome = true; }
        // The home window: always without a tray icon (never over the welcome window), else if it was
        // open when Hesper last quit
        if (!welcome && (!HasTray || model.Settings.Detached))
        {
            OpenHome();
            if (autostart && !HasTray) home?.Minimize();  // at login: there, not in the way
        }
        OnChanged();
    }

    readonly Dictionary<IntPtr, Action> actions = [];

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnMenuItem(IntPtr item, IntPtr data)
    {
        if (GCHandle.FromIntPtr(data).Target is TrayApp app && app.actions.TryGetValue(item, out var a))
            MainLoop.Instance.Post(_ => a(), null);
    }

    string T(string key) => I18n.T(model.Settings.AppLanguage, key);

    void OnChanged()
    {
        var recording = model.Recorder != null;
        if (HasTray)
        {
            Native.app_indicator_set_icon_full(indicator, recording ? "hesper-tray-recording" : "hesper-tray", recording ? "Hesper (recording)" : "Hesper");
            Native.gtk_widget_set_visible(record, recording ? 0 : 1);
            Native.gtk_widget_set_visible(stop, recording ? 1 : 0);
            Native.gtk_widget_set_sensitive(record, model.Ready ? 1 : 0);
        }
        if (recording) { live.Start(); label.Start(); } else { live.Stop(); label.Stop(); }
        UpdateLabel();
        // Ctrl+Alt+M marks a moment only while recording: the rest of the time the keys are the other apps'
        var mark = recording ? model.Settings.MarkHotkey : "";
        if (mark != markKeySet) { markKey.Set(mark); markKeySet = mark; }
        WebWindow.Broadcast("state", model.State);
    }

    /// Beside the icon while recording: the time recorded (⏸ when paused).
    void UpdateLabel()
    {
        if (!HasTray) return;
        var r = model.Recorder;
        var text = r == null ? "" : (r.Paused ? "⏸ " : "") + Clock(r.Elapsed);
        Native.app_indicator_set_label(indicator, text, "00:00:00");
    }

    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    /// The tray menu's "Open Hesper", hesper://open, launching Hesper again: the home window.
    void OpenHome()
    {
        if (home is { IsClosed: false } open) { open.Show(); return; }
        var w = new WebWindow(model, "home/detached", 400, 600);
        w.SetMinimumSize(340, 440);
        if (model.Settings.DetachedBounds is { Length: 4 } b) w.Bounds = new Rectangle(b[0], b[1], Math.Max(340, b[2]), Math.Max(440, b[3]));
        if (model.Settings.DetachedOnTop && !Platform.Wayland) w.SetKeepAbove(true);
        w.Closing = () =>
        {
            model.SetDetachedBounds(w.Bounds);
            if (HasTray || quitting) return false;
            w.Hide();  // no tray icon: the window is the app, it goes on in the background
            return true;
        };
        w.Closed += () =>
        {
            if (home == w) home = null;
            if (!quitting) model.SetDetached(false);  // closed by the user: not reopened at the next launch
        };
        home = w;
        w.Show();
        model.SetDetached(true);
    }

    /// A view of the home page (to-dos, the week, a meeting's brief) in the home window.
    void ShowView(string view)
    {
        OpenHome();
        home?.Push("view", view);
    }

    void Show(string page, int width, int height, string title)
    {
        if (WebWindow.Find(page) is { } w) { w.Show(); return; }
        var n = new WebWindow(model, page, width, height, title);
        n.SetMinimumSize(480, 420);
        n.Show();
    }

    void ShowMeeting(string id) => Show("meeting/" + Uri.EscapeDataString(id), 760, 760, "Hesper");
    void ShowSettings(string pane) => Show("settings" + (pane.Length > 0 ? "/" + pane : ""), 780, 580, "Hesper settings");
    void ShowWelcome() => Show("welcome", 640, 600, "Welcome to Hesper");

    async void Quit()
    {
        if (model.Recorder != null && !Program.AskYesNo(T("A meeting is being recorded. Stop it and quit? Its notes won't be created now.")))
            return;
        quitting = true;
        if (home is { IsClosed: false } w) model.SetDetachedBounds(w.Bounds);
        if (model.Recorder is { } rec)
        {
            await rec.StopAsync();
            rec.Dispose();
        }
        model.Player.Stop();
        Platform.KeepAwake(false);
        hotkey.Dispose();
        markKey.Dispose();
        foreach (var open in WebWindow.All.ToList()) open.Close();
        Native.gtk_main_quit();
    }
}
