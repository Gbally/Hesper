using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Hesper;

/// A window showing one of the app's pages (the Windows app's HTML, inside this program, served as
/// hesper-app://app/), in a GTK window with WebKitGTK, and the bridge to it. The pages talk to
/// `window.chrome.webview` as in WebView2 on Windows: a small script provides it here, over WebKit's
/// message handler. The page posts {id, cmd, args}, gets {id, result}; the app pushes {event, data}.
sealed class WebWindow
{
    const string Scheme = "hesper-app", Origin = Scheme + "://app/";
    static IntPtr context;  // one web context for every window: ephemeral (nothing written to disk)
    static readonly List<WebWindow> open = [];

    public IntPtr Window { get; }
    readonly IntPtr view;
    readonly AppModel model;
    readonly string page;
    GCHandle self;
    bool ready, closed;

    /// The window is gone (closed by the user or the app).
    public event Action? Closed;
    /// A close button click: return true to keep the window (it's hidden instead, or kept).
    public Func<bool>? Closing;

    /// WebView2's window.chrome.webview, over WebKit's message handler "hesper"
    const string Bridge = """
        (() => {
          const listeners = [];
          window.chrome = window.chrome || {};
          window.chrome.webview = {
            postMessage: m => window.webkit.messageHandlers.hesper.postMessage(JSON.stringify(m)),
            addEventListener: (type, f) => { if (type === "message") listeners.push(f); },
          };
          window.__hesperReceive = data => { for (const f of listeners) f({ data }); };
        })();
        """;

    public unsafe WebWindow(AppModel model, string page, int width, int height, string title = "Hesper")
    {
        this.model = model;
        this.page = page;
        self = GCHandle.Alloc(this);
        var me = GCHandle.ToIntPtr(self);
        if (context == IntPtr.Zero) CreateContext(model);

        Window = Native.gtk_window_new(0 /* GTK_WINDOW_TOPLEVEL */);
        Native.gtk_window_set_title(Window, title);
        Native.gtk_window_set_default_size(Window, width, height);
        Native.gtk_window_set_position(Window, 1 /* GTK_WIN_POS_CENTER */);
        if (File.Exists(Identity.Icon)) Native.gtk_window_set_icon_from_file(Window, Identity.Icon, out _);

        var content = Native.webkit_user_content_manager_new();
        var script = Native.webkit_user_script_new(Bridge, 1 /* top frame */, 0 /* at document start */, IntPtr.Zero, IntPtr.Zero);
        Native.webkit_user_content_manager_add_script(content, script);
        Native.webkit_user_script_unref(script);
        Native.Connect(content, "script-message-received::hesper", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&OnScriptMessage, me);
        Native.webkit_user_content_manager_register_script_message_handler(content, "hesper");

        view = Native.g_object_new(Native.webkit_web_view_get_type(), "web-context", context, "user-content-manager", content, IntPtr.Zero);
        Native.g_object_unref(content);
        var settings = Native.webkit_web_view_get_settings(view);
        var devtools = Environment.GetEnvironmentVariable("HESPER_DEVTOOLS") == "1";
        Native.webkit_settings_set_enable_developer_extras(settings, devtools ? 1 : 0);
        Native.webkit_settings_set_enable_write_console_messages_to_stdout(settings, devtools ? 1 : 0);
        var ink = new Native.GdkRgba { Red = 0x1B / 255.0, Green = 0x1F / 255.0, Blue = 0x2E / 255.0, Alpha = 1 };  // no white flash
        Native.webkit_web_view_set_background_color(view, ref ink);
        Native.Connect(view, "load-changed", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&OnLoadChanged, me);
        Native.Connect(view, "notify::title", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&OnTitle, me);
        Native.Connect(view, "decide-policy", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, int>)&OnDecidePolicy, me);
        if (!devtools) Native.Connect(view, "context-menu", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>)&OnContextMenu, me);

        Native.gtk_container_add(Window, view);
        Native.Connect(Window, "delete-event", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int>)&OnDelete, me);
        Native.Connect(Window, "destroy", (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnDestroy, me);
        open.Add(this);
        Native.webkit_web_view_load_uri(view, Origin + "index.html#" + page);
    }

    /// The web context: no data on disk, the pages served from inside the program.
    static unsafe void CreateContext(AppModel model)
    {
        context = Native.webkit_web_context_new_ephemeral();
        var security = Native.webkit_web_context_get_security_manager(context);
        Native.webkit_security_manager_register_uri_scheme_as_secure(security, Scheme);
        if (Native.HasSymbol(Native.WebKitLib, "webkit_security_manager_register_uri_scheme_as_cors_enabled"))
            Native.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, Scheme);
        schemeModel = model;
        Native.webkit_web_context_register_uri_scheme(context, Scheme, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnScheme, IntPtr.Zero, IntPtr.Zero);
    }

    static AppModel? schemeModel;

    static WebWindow? From(IntPtr data) => data == IntPtr.Zero ? null : GCHandle.FromIntPtr(data).Target as WebWindow;

    // ---------- the pages' files: hesper-app://app/PATH

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnScheme(IntPtr request, IntPtr data)
    {
        try
        {
            var path = (Native.Str(Native.webkit_uri_scheme_request_get_path(request)) ?? "").TrimStart('/');
            var (bytes, type) = Serve(Uri.UnescapeDataString(path));
            if (bytes == null)
            {
                Native.webkit_uri_scheme_request_finish_error(request, Native.g_error_new_literal(Native.g_quark_from_string("hesper"), 404, "Not found"));
                return;
            }
            var copy = Native.g_malloc((nuint)Math.Max(1, bytes.Length));  // GLib frees it with the stream
            Marshal.Copy(bytes, 0, copy, bytes.Length);
            var stream = Native.g_memory_input_stream_new_from_data(copy, bytes.Length, Native.GFree);
            Native.webkit_uri_scheme_request_finish(request, stream, bytes.Length, type);
            Native.g_object_unref(stream);
        }
        catch (Exception e)
        {
            Paths.WriteLog("page file: " + e.Message);
            Native.webkit_uri_scheme_request_finish_error(request, Native.g_error_new_literal(Native.g_quark_from_string("hesper"), 500, e.Message));
        }
    }

    static (byte[]? Bytes, string Type) Serve(string path)
    {
        // A meeting's voice sample (naming the speakers): clip/MEETING/FILE.wav
        if (path.StartsWith("clip/"))
        {
            var parts = path.Split('/');
            var file = parts.Length == 3 && schemeModel != null ? Meetings.ClipFile(schemeModel.Settings.NotesFolder, parts[1], parts[2]) : null;
            return file == null ? (null, "") : (File.ReadAllBytes(file), "audio/wav");
        }
        using var stream = typeof(WebWindow).Assembly.GetManifestResourceStream("ui/" + (path.Length > 0 ? path : "index.html"));
        if (stream == null) return (null, "");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var type = Path.GetExtension(path) switch
        {
            ".html" or "" => "text/html", ".css" => "text/css", ".js" => "text/javascript", ".json" => "application/json",
            ".svg" => "image/svg+xml", ".woff2" => "font/woff2", _ => "application/octet-stream",
        };
        return (ms.ToArray(), type);
    }

    // ---------- the bridge

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnScriptMessage(IntPtr manager, IntPtr result, IntPtr data)
    {
        var json = Native.TakeStr(Native.jsc_value_to_string(Native.webkit_javascript_result_get_js_value(result)));
        // Handled after WebKit's callback returns: a command may open a dialog (a folder to choose)
        if (From(data) is { } w && json != null) MainLoop.Instance.Post(_ => w.OnMessage(json), null);
    }

    async void OnMessage(string json)
    {
        var msg = JsonNode.Parse(json)!.AsObject();
        var id = msg["id"]?.GetValue<int>();
        var cmd = msg["cmd"]?.GetValue<string>() ?? "";
        JsonNode? result = null;
        string? error = null;
        try
        {
            result = cmd switch
            {
                "close" => Do(Close),
                "hide" => Do(Hide),
                _ => await model.HandleAsync(cmd, msg["args"] as JsonObject ?? []),
            };
        }
        catch (Exception e)
        {
            Paths.WriteLog($"{cmd}: {e}");
            error = e.Message;
        }
        if (id != null && !closed)  // also before the page has loaded: it asks for its state while loading
            Post(new JsonObject { ["id"] = id, ["result"] = result, ["error"] = error });
    }

    static JsonNode? Do(Action a) { a(); return null; }

    void Post(JsonObject message)
    {
        var script = "window.__hesperReceive(" + message.ToJsonString() + ")";
        if (Native.HasSymbol(Native.WebKitLib, "webkit_web_view_evaluate_javascript"))  // WebKitGTK 2.40+
            Native.webkit_web_view_evaluate_javascript(view, script, -1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        else Native.webkit_web_view_run_javascript(view, script, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    public void Push(string name, JsonNode? data)
    {
        if (ready && !closed && Visible) Post(new JsonObject { ["event"] = name, ["data"] = data });
    }

    /// Sends an event to every open page.
    public static void Broadcast(string name, Func<JsonNode?> data)
    {
        var shown = open.Where(w => w.Visible && w.ready && !w.closed).ToList();
        if (shown.Count == 0) return;
        var d = data();
        foreach (var w in shown) w.Push(name, d?.DeepClone());
    }

    public static WebWindow? Find(string page) => open.FirstOrDefault(w => w.page == page && !w.closed);

    public static IEnumerable<WebWindow> All => open.Where(w => !w.closed);

    // ---------- the window

    public bool Loaded => ready;
    public bool Visible => !closed && Native.gtk_widget_get_visible(Window) != 0;
    public bool IsClosed => closed;

    public void Show()
    {
        if (closed) return;
        Native.gtk_widget_show_all(Window);
        Native.gtk_window_present(Window);
        Push("state", null);  // the page asks for the full state on this event
    }

    public void Hide() { if (!closed) Native.gtk_widget_hide(Window); }

    public void Close() { if (!closed) Native.gtk_widget_destroy(Window); }

    public void Minimize() { if (!closed) Native.gtk_window_iconify(Window); }

    public void SetKeepAbove(bool on) { if (!closed) Native.gtk_window_set_keep_above(Window, on ? 1 : 0); }

    public void SetMinimumSize(int width, int height) => Native.gtk_widget_set_size_request(Window, width, height);

    /// Where the window is and its size (the position is 0, 0 on Wayland: apps aren't told).
    public Rectangle Bounds
    {
        get
        {
            Native.gtk_window_get_position(Window, out var x, out var y);
            Native.gtk_window_get_size(Window, out var w, out var h);
            return new Rectangle(x, y, w, h);
        }
        set
        {
            Native.gtk_window_resize(Window, value.Width, value.Height);
            if (!Platform.Wayland) Native.gtk_window_move(Window, value.X, value.Y);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnLoadChanged(IntPtr view, int loadEvent, IntPtr data)
    {
        if (loadEvent == 3 /* WEBKIT_LOAD_FINISHED */ && From(data) is { } w) w.ready = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnTitle(IntPtr view, IntPtr spec, IntPtr data)
    {
        // The page's title, in the app's language
        if (From(data) is { closed: false } w && Native.Str(Native.webkit_web_view_get_title(view)) is { Length: > 0 } title)
            Native.gtk_window_set_title(w.Window, title);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static int OnDecidePolicy(IntPtr view, IntPtr decision, int type, IntPtr data)
    {
        if (type == 2) return 0;  // WEBKIT_POLICY_DECISION_TYPE_RESPONSE: the default
        if (type == 1) { Native.webkit_policy_decision_ignore(decision); return 1; }  // a new window: none
        var request = Native.webkit_navigation_action_get_request(Native.webkit_navigation_policy_decision_get_navigation_action(decision));
        var uri = Native.Str(Native.webkit_uri_request_get_uri(request)) ?? "";
        if (uri.StartsWith(Origin)) return 0;
        Native.webkit_policy_decision_ignore(decision);  // links open nowhere inside (the pages ask the app to open them)
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static int OnContextMenu(IntPtr view, IntPtr menu, IntPtr ev, IntPtr hit, IntPtr data) => 1;  // no "Reload", "Inspect"…

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static int OnDelete(IntPtr window, IntPtr ev, IntPtr data)
    {
        var w = From(data);
        try { return w?.Closing?.Invoke() == true ? 1 : 0; }
        catch (Exception e) { Paths.WriteLog("closing: " + e); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnDestroy(IntPtr window, IntPtr data)
    {
        if (From(data) is not { } w) return;
        w.closed = true;
        open.Remove(w);
        try { w.Closed?.Invoke(); }
        catch (Exception e) { Paths.WriteLog("closed: " + e); }
        w.self.Free();
    }

    // ---------- a picture of the page (Hesper --capture, to compare with the Mac and Windows)

    TaskCompletionSource<bool>? snapshot;
    string snapshotFile = "";

    public unsafe Task<bool> CapturePng(string path)
    {
        snapshotFile = path;
        snapshot = new TaskCompletionSource<bool>();
        Native.webkit_web_view_get_snapshot(view, 0 /* visible */, 0, IntPtr.Zero, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&OnSnapshot,
                                            GCHandle.ToIntPtr(self));
        return snapshot.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnSnapshot(IntPtr view, IntPtr result, IntPtr data)
    {
        if (From(data) is not { } w) return;
        var surface = Native.webkit_web_view_get_snapshot_finish(view, result, out var error);
        if (surface == IntPtr.Zero) { Console.WriteLine("snapshot: " + Native.ErrorText(error)); w.snapshot?.TrySetResult(false); return; }
        var ok = Native.cairo_surface_write_to_png(surface, w.snapshotFile) == 0;
        Native.cairo_surface_destroy(surface);
        w.snapshot?.TrySetResult(ok);
    }
}
