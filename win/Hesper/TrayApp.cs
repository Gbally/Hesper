namespace Hesper;

/// The app: an icon in the notification area. A click opens the popover (as the Mac's menu bar
/// icon does), or brings forward the home view's own window when it's detached; a right click a
/// short menu. Meetings and Settings open in their own windows.
sealed class TrayApp : ApplicationContext
{
    readonly AppModel model = new();
    readonly NotifyIcon tray = new();
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem record = new("Record"), stop = new("Stop and create notes") { Visible = false };
    readonly System.Windows.Forms.Timer live = new() { Interval = 200 }, coalesce = new() { Interval = 150 };
    readonly Icon idleIcon = Brand.Icon(32), recordingIcon = Brand.Icon(32, recording: true);
    readonly Popover popover;
    Action? onBalloonClick;  // what a click on the last notification does
    readonly Hotkey hotkey = new(), markKey = new();
    string markKeySet = "";   // the mark shortcut registered now: only while recording
    WebWindow? detached;      // the home view in a normal window (Settings.Detached)
    bool quitting;

    public TrayApp(string instance = "", string? link = null)
    {
        popover = new Popover(model);
        menu.Items.Add("Open Hesper", null, (_, _) => OpenHome());
        menu.Items.Add(record);
        menu.Items.Add(stop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings(""));
        menu.Items.Add("Quit Hesper", null, (_, _) => Quit());
        record.Click += (_, _) => model.StartRecording("");
        stop.Click += (_, _) => model.StopRecording();

        tray.Icon = idleIcon;
        tray.Text = "Hesper";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenHome(); };
        tray.BalloonTipClicked += (_, _) => onBalloonClick?.Invoke();
        tray.BalloonTipClosed += (_, _) => onBalloonClick = null;

        model.Changed += () => { if (!coalesce.Enabled) coalesce.Start(); };  // progress lines come fast: at most ~6 redraws a second
        coalesce.Tick += (_, _) => { coalesce.Stop(); OnChanged(); };
        model.Notify += (title, text, action) =>
        {
            onBalloonClick = action;
            tray.ShowBalloonTip(10_000, title, text, ToolTipIcon.None);
        };
        hotkey.Pressed += model.ToggleRecording;
        hotkey.Set(model.Settings.Hotkey);
        model.HotkeyChanged += () => hotkey.Set(model.Settings.Hotkey);
        markKey.Pressed += model.MarkMoment;
        model.OpenMeetingWindow += ShowMeeting;
        model.OpenSettingsWindow += ShowSettings;
        model.OpenWelcomeWindow += ShowWelcome;
        model.QuitRequested += Quit;
        model.ShowView += ShowView;
        model.DetachRequested += Detach;
        model.KeepOnTopChanged += on => { if (detached is { IsDisposed: false }) detached.TopMost = on; };
        live.Tick += (_, _) => WebWindow.Broadcast("live", model.Live);

        // hesper:// links: opened while Hesper runs (another Hesper.exe hands them over), or the one it was started with
        var ui = SynchronizationContext.Current;
        LinkPipe.Listen(instance, url => ui?.Post(_ => model.HandleLink(url), null));
        if (link != null)
        {
            var once = new System.Windows.Forms.Timer { Interval = 1500 };
            once.Tick += (_, _) => { once.Dispose(); model.HandleLink(link); };
            once.Start();
        }

        model.Settings.WriteConfig();
        // A tray app that isn't there after a restart notices no call and reminds of nothing: on by
        // default from the first launch, and on the welcome window's last page to turn off
        if (model.Settings.IsNew && Environment.GetEnvironmentVariable("HESPER_HOME") == null) Startup.Enabled = true;
        if (!model.Ready && model.Settings.Welcomed && Setup.WasInstalled)
        {
            // An update (or a new engine): set up again in the background; the popover shows it
            var later = new System.Windows.Forms.Timer { Interval = 300 };
            later.Tick += (_, _) => { later.Dispose(); _ = model.RunSetup(); };
            later.Start();
        }
        else if (!model.Ready || !model.Settings.Welcomed) ShowWelcome();
        // Detached when Hesper last quit: the window again (never over the welcome window)
        if (model.Settings.Detached && model.Settings.Welcomed && (model.Ready || Setup.WasInstalled)) Detach();
    }

    void OnChanged()
    {
        var recording = model.Recorder != null;
        tray.Icon = recording ? recordingIcon : idleIcon;
        tray.Text = model.Recorder?.Paused == true ? "Hesper ⏸" : "Hesper";
        record.Visible = !recording;
        stop.Visible = recording;
        record.Enabled = model.Ready;
        if (recording) live.Start(); else live.Stop();
        // Ctrl+Alt+M marks a moment only while recording: the rest of the time the keys are the other apps'
        var mark = recording ? model.Settings.MarkHotkey : "";
        if (mark != markKeySet) { markKey.Set(mark); markKeySet = mark; }
        WebWindow.Broadcast("state", model.State);
    }

    /// The icon, "Open Hesper", hesper://open: the detached window when there is one, else the popover.
    void OpenHome()
    {
        if (detached is { IsDisposed: false } w) Bring(w);
        else popover.Toggle();
    }

    static void Bring(Form w)
    {
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Show();
        w.Activate();
    }

    /// A view of the home page (to-dos, the week, a meeting's brief) where the home view is.
    void ShowView(string view)
    {
        if (detached is { IsDisposed: false } w)
        {
            Bring(w);
            w.Push("view", view);
        }
        else popover.ShowPage(view);
    }

    /// The home view in a normal window (the icon next to the gear): resizable, in the taskbar and
    /// Alt-Tab, where it last was; closing it goes back to the popover. As the Mac's Windows.showMain.
    void Detach()
    {
        popover.Hide();
        if (detached is { IsDisposed: false } open) { Bring(open); return; }
        var b = model.Settings.DetachedBounds;
        var w = new WebWindow(model, "home/detached", new Size(400, 600))
        {
            Text = "Hesper", MinimumSize = new Size(340, 440), TopMost = model.Settings.DetachedOnTop,
        };
        if (b is { Length: 4 } && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(b[0], b[1], b[2], b[3]))))
        {
            w.StartPosition = FormStartPosition.Manual;
            w.Bounds = new Rectangle(b[0], b[1], Math.Max(340, b[2]), Math.Max(440, b[3]));
        }
        w.FormClosing += (_, e) =>
        {
            model.SetDetachedBounds(w.WindowState == FormWindowState.Normal ? w.Bounds : w.RestoreBounds);
            // Closed by the user: back to the popover. Quitting or Windows shutting down: open again next time
            if (!quitting && e.CloseReason == CloseReason.UserClosing) model.SetDetached(false);
        };
        w.FormClosed += (_, _) => { if (detached == w) detached = null; };
        detached = w;
        w.Show();
        model.SetDetached(true);
    }

    void Show(string page, Size size, string title)
    {
        if (WebWindow.Find(page) is { } w)
        {
            w.WindowState = FormWindowState.Normal;
            w.Activate();
            return;
        }
        popover.Hide();
        new WebWindow(model, page, size) { Text = title, MinimumSize = new Size(480, 420) }.Show();
    }

    void ShowMeeting(string id) => Show("meeting/" + Uri.EscapeDataString(id), new Size(760, 760), "Hesper");
    void ShowSettings(string pane) => Show("settings" + (pane.Length > 0 ? "/" + pane : ""), new Size(780, 580), "Hesper settings");
    void ShowWelcome() => Show("welcome", new Size(640, 600), "Welcome to Hesper");

    async void Quit()
    {
        if (model.Recorder != null && MessageBox.Show("A meeting is being recorded. Stop it and quit? Its notes won't be created now.",
                "Hesper", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        quitting = true;
        if (detached is { IsDisposed: false } w)
            model.SetDetachedBounds(w.WindowState == FormWindowState.Normal ? w.Bounds : w.RestoreBounds);
        if (model.Recorder is { } rec)
        {
            await rec.StopAsync();
            rec.Dispose();
        }
        model.Player.Stop();
        tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray.Dispose();
            menu.Dispose();
            live.Dispose();
            popover.Dispose();
            hotkey.Dispose();
            markKey.Dispose();
        }
        base.Dispose(disposing);
    }
}
