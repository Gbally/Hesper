using System.Reflection;
using System.Runtime.InteropServices;

namespace Hesper;

/// The Linux libraries Hesper calls (C APIs, through P/Invoke): GLib, GTK 3, WebKitGTK 4.1 (or 4.0),
/// AppIndicator, libnotify, cairo, PulseAudio's simple API, X11. Strings go as UTF-8 (DllImport's
/// default on Linux); returned strings are read with Str(). The names below are logical: Resolve()
/// finds the installed library, so a missing optional one (AppIndicator, libnotify, X11) only turns
/// a feature off.
static unsafe class Native
{
    const string Gtk = "gtk", Gdk = "gdk", GLib = "glib", GObject = "gobject", Gio = "gio", WebKit = "webkit", Jsc = "jsc",
                 Indicator = "appindicator", Notify = "notify", Cairo = "cairo", PulseSimple = "pulse-simple", Pulse = "pulse",
                 X11 = "x11", Xss = "xss";

    static readonly Dictionary<string, string[]> Files = new()
    {
        [Gtk] = ["libgtk-3.so.0"], [Gdk] = ["libgdk-3.so.0"], [GLib] = ["libglib-2.0.so.0"], [GObject] = ["libgobject-2.0.so.0"],
        [Gio] = ["libgio-2.0.so.0"],
        [WebKit] = ["libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37"],
        [Jsc] = ["libjavascriptcoregtk-4.1.so.0", "libjavascriptcoregtk-4.0.so.18"],
        [Indicator] = ["libayatana-appindicator3.so.1", "libappindicator3.so.1"],
        [Notify] = ["libnotify.so.4"], [Cairo] = ["libcairo.so.2"],
        [PulseSimple] = ["libpulse-simple.so.0"], [Pulse] = ["libpulse.so.0"],
        [X11] = ["libX11.so.6"], [Xss] = ["libXss.so.1"],
    };

    static readonly Dictionary<string, IntPtr> loaded = [];

    public static void Init() => NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);

    static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        lock (loaded)
        {
            if (loaded.TryGetValue(name, out var h)) return h;
            if (!Files.TryGetValue(name, out var files)) return IntPtr.Zero;  // a real file name: the default search
            foreach (var f in files)
                if (NativeLibrary.TryLoad(f, out h)) return loaded[name] = h;
            return IntPtr.Zero;  // DllNotFoundException on the first call
        }
    }

    /// Whether a library (logical name) can be loaded: optional features check first.
    public static bool Has(string name) => Resolve(name, typeof(Native).Assembly, null) != IntPtr.Zero;

    public static bool HasSymbol(string library, string symbol) =>
        Resolve(library, typeof(Native).Assembly, null) is var h && h != IntPtr.Zero && NativeLibrary.TryGetExport(h, symbol, out _);

    public const string IndicatorLib = Indicator, NotifyLib = Notify, X11Lib = X11, XssLib = Xss, WebKitLib = WebKit, PulseLib = PulseSimple;

    /// The libraries without which there's no window: their packages, to say what to install.
    public static string? MissingDesktop()
    {
        if (!Has(Gtk)) return "GTK 3 (libgtk-3-0)";
        if (!Has(WebKit) || !Has(Jsc)) return "WebKitGTK (libwebkit2gtk-4.1-0 on Debian / Ubuntu, webkit2gtk4.1 on Fedora)";
        return null;
    }

    public static string? Str(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);

    /// A string GLib allocated: read, then freed.
    public static string? TakeStr(IntPtr p)
    {
        var s = Str(p);
        if (p != IntPtr.Zero) g_free(p);
        return s;
    }

    /// A GError's message, and the error freed.
    public static string ErrorText(IntPtr error)
    {
        if (error == IntPtr.Zero) return "";
        var text = Str(Marshal.ReadIntPtr(error, 8)) ?? "";  // struct GError { GQuark domain; gint code; gchar *message; }
        g_error_free(error);
        return text;
    }

    // ---------- GLib / GObject / GIO
    [DllImport(GLib)] public static extern uint g_idle_add_full(int priority, IntPtr function, IntPtr data, IntPtr notify);
    [DllImport(GLib)] public static extern void g_free(IntPtr p);
    [DllImport(GLib)] public static extern IntPtr g_malloc(nuint size);
    [DllImport(GLib)] public static extern void g_error_free(IntPtr error);
    [DllImport(GLib)] public static extern uint g_quark_from_string(string s);
    [DllImport(GLib)] public static extern IntPtr g_error_new_literal(uint domain, int code, string message);
    [DllImport(GLib)] public static extern void g_set_prgname(string name);
    [DllImport(GLib)] public static extern void g_set_application_name(string name);
    [DllImport(GLib)] public static extern IntPtr g_variant_parse(IntPtr type, string text, IntPtr limit, IntPtr endptr, out IntPtr error);
    [DllImport(GLib)] public static extern IntPtr g_variant_new_string(string s);
    [DllImport(GLib)] public static extern IntPtr g_variant_get_child_value(IntPtr v, nuint index);
    [DllImport(GLib)] public static extern IntPtr g_variant_get_variant(IntPtr v);
    [DllImport(GLib)] public static extern IntPtr g_variant_get_type_string(IntPtr v);
    [DllImport(GLib)] public static extern int g_variant_get_boolean(IntPtr v);
    [DllImport(GLib)] public static extern uint g_variant_get_uint32(IntPtr v);
    [DllImport(GLib)] public static extern ulong g_variant_get_uint64(IntPtr v);
    [DllImport(GLib)] public static extern void g_variant_unref(IntPtr v);

    [DllImport(GObject)] public static extern void g_object_unref(IntPtr o);
    [DllImport(GObject)] public static extern IntPtr g_object_ref(IntPtr o);
    [DllImport(GObject)] public static extern nuint g_signal_connect_data(IntPtr instance, string signal, IntPtr handler, IntPtr data, IntPtr destroy, int flags);
    // g_object_new / g_object_set are variadic: these fixed forms (pointers and ints, NULL-terminated)
    // pass their arguments as the C calls would on x86-64 and arm64 Linux
    [DllImport(GObject)] public static extern IntPtr g_object_new(nuint type, string p1, IntPtr v1, string p2, IntPtr v2, IntPtr end);
    [DllImport(GObject)] public static extern void g_object_set(IntPtr o, string property, int value, IntPtr end);

    public static nuint Connect(IntPtr instance, string signal, IntPtr handler, IntPtr data) =>
        g_signal_connect_data(instance, signal, handler, data, IntPtr.Zero, 0);

    [DllImport(Gio)] public static extern IntPtr g_bus_get_sync(int busType, IntPtr cancellable, out IntPtr error);
    [DllImport(Gio)] public static extern IntPtr g_dbus_connection_call_sync(IntPtr connection, string busName, string objectPath, string iface,
        string method, IntPtr parameters, IntPtr replyType, int flags, int timeoutMs, IntPtr cancellable, out IntPtr error);
    [DllImport(Gio)] public static extern IntPtr g_memory_input_stream_new_from_data(IntPtr data, nint length, IntPtr destroy);

    /// g_free's address, as a GDestroyNotify.
    public static IntPtr GFree => NativeLibrary.GetExport(Resolve(GLib, typeof(Native).Assembly, null), "g_free");

    // ---------- GTK / GDK
    [DllImport(Gtk)] public static extern int gtk_init_check(IntPtr argc, IntPtr argv);
    [DllImport(Gtk)] public static extern void gtk_main();
    [DllImport(Gtk)] public static extern void gtk_main_quit();
    [DllImport(Gtk)] public static extern IntPtr gtk_window_new(int type);
    [DllImport(Gtk)] public static extern void gtk_window_set_title(IntPtr w, string title);
    [DllImport(Gtk)] public static extern void gtk_window_set_default_size(IntPtr w, int width, int height);
    [DllImport(Gtk)] public static extern void gtk_window_resize(IntPtr w, int width, int height);
    [DllImport(Gtk)] public static extern void gtk_window_move(IntPtr w, int x, int y);
    [DllImport(Gtk)] public static extern void gtk_window_get_size(IntPtr w, out int width, out int height);
    [DllImport(Gtk)] public static extern void gtk_window_get_position(IntPtr w, out int x, out int y);
    [DllImport(Gtk)] public static extern void gtk_window_set_position(IntPtr w, int position);
    [DllImport(Gtk)] public static extern void gtk_window_set_keep_above(IntPtr w, int setting);
    [DllImport(Gtk)] public static extern void gtk_window_present(IntPtr w);
    [DllImport(Gtk)] public static extern void gtk_window_close(IntPtr w);
    [DllImport(Gtk)] public static extern void gtk_window_iconify(IntPtr w);
    [DllImport(Gtk)] public static extern int gtk_window_set_icon_from_file(IntPtr w, string file, out IntPtr error);
    [DllImport(Gtk)] public static extern void gtk_window_set_default_icon_name(string name);
    [DllImport(Gtk)] public static extern void gtk_container_add(IntPtr container, IntPtr widget);
    [DllImport(Gtk)] public static extern void gtk_widget_show_all(IntPtr w);
    [DllImport(Gtk)] public static extern void gtk_widget_hide(IntPtr w);
    [DllImport(Gtk)] public static extern void gtk_widget_destroy(IntPtr w);
    [DllImport(Gtk)] public static extern int gtk_widget_get_visible(IntPtr w);
    [DllImport(Gtk)] public static extern void gtk_widget_set_visible(IntPtr w, int visible);
    [DllImport(Gtk)] public static extern void gtk_widget_set_sensitive(IntPtr w, int sensitive);
    [DllImport(Gtk)] public static extern void gtk_widget_set_size_request(IntPtr w, int width, int height);
    [DllImport(Gtk)] public static extern IntPtr gtk_menu_new();
    [DllImport(Gtk)] public static extern IntPtr gtk_menu_item_new_with_label(string label);
    [DllImport(Gtk)] public static extern void gtk_menu_item_set_label(IntPtr item, string label);
    [DllImport(Gtk)] public static extern IntPtr gtk_separator_menu_item_new();
    [DllImport(Gtk)] public static extern void gtk_menu_shell_append(IntPtr menu, IntPtr item);
    [DllImport(Gtk)] public static extern IntPtr gtk_clipboard_get(IntPtr selection);
    [DllImport(Gtk)] public static extern void gtk_clipboard_set_text(IntPtr clipboard, string text, int length);
    [DllImport(Gtk)] public static extern void gtk_clipboard_store(IntPtr clipboard);
    [DllImport(Gtk)] public static extern IntPtr gtk_file_chooser_native_new(string title, IntPtr parent, int action, string? accept, string? cancel);
    [DllImport(Gtk)] public static extern int gtk_native_dialog_run(IntPtr dialog);
    [DllImport(Gtk)] public static extern IntPtr gtk_file_chooser_get_filename(IntPtr chooser);
    [DllImport(Gtk)] public static extern void gtk_file_chooser_add_filter(IntPtr chooser, IntPtr filter);
    [DllImport(Gtk)] public static extern IntPtr gtk_file_filter_new();
    [DllImport(Gtk)] public static extern void gtk_file_filter_set_name(IntPtr filter, string name);
    [DllImport(Gtk)] public static extern void gtk_file_filter_add_pattern(IntPtr filter, string pattern);
    [DllImport(Gtk)] public static extern IntPtr gtk_message_dialog_new(IntPtr parent, int flags, int type, int buttons, string format, string text);
    [DllImport(Gtk)] public static extern int gtk_dialog_run(IntPtr dialog);
    [DllImport(Gtk)] public static extern IntPtr gtk_settings_get_default();
    [DllImport(Gdk)] public static extern IntPtr gdk_atom_intern(string name, int onlyIfExists);

    public const int GtkResponseAccept = -3, GtkResponseYes = -8;

    // ---------- WebKitGTK (4.1 or 4.0: the same calls)
    [DllImport(WebKit)] public static extern nuint webkit_web_view_get_type();
    [DllImport(WebKit)] public static extern IntPtr webkit_web_context_new_ephemeral();
    [DllImport(WebKit)] public static extern void webkit_web_context_register_uri_scheme(IntPtr context, string scheme, IntPtr callback, IntPtr data, IntPtr destroy);
    [DllImport(WebKit)] public static extern IntPtr webkit_web_context_get_security_manager(IntPtr context);
    [DllImport(WebKit)] public static extern void webkit_security_manager_register_uri_scheme_as_secure(IntPtr manager, string scheme);
    [DllImport(WebKit)] public static extern void webkit_security_manager_register_uri_scheme_as_cors_enabled(IntPtr manager, string scheme);
    [DllImport(WebKit)] public static extern IntPtr webkit_uri_scheme_request_get_path(IntPtr request);
    [DllImport(WebKit)] public static extern void webkit_uri_scheme_request_finish(IntPtr request, IntPtr stream, long length, string mime);
    [DllImport(WebKit)] public static extern void webkit_uri_scheme_request_finish_error(IntPtr request, IntPtr error);
    [DllImport(WebKit)] public static extern IntPtr webkit_user_content_manager_new();
    [DllImport(WebKit)] public static extern int webkit_user_content_manager_register_script_message_handler(IntPtr manager, string name);
    [DllImport(WebKit)] public static extern void webkit_user_content_manager_add_script(IntPtr manager, IntPtr script);
    [DllImport(WebKit)] public static extern IntPtr webkit_user_script_new(string source, int frames, int time, IntPtr allow, IntPtr block);
    [DllImport(WebKit)] public static extern void webkit_user_script_unref(IntPtr script);
    [DllImport(WebKit)] public static extern IntPtr webkit_javascript_result_get_js_value(IntPtr result);
    [DllImport(WebKit)] public static extern void webkit_web_view_load_uri(IntPtr view, string uri);
    [DllImport(WebKit)] public static extern IntPtr webkit_web_view_get_title(IntPtr view);
    [DllImport(WebKit)] public static extern IntPtr webkit_web_view_get_settings(IntPtr view);
    [DllImport(WebKit)] public static extern void webkit_web_view_set_background_color(IntPtr view, ref GdkRgba color);
    [DllImport(WebKit)] public static extern void webkit_web_view_run_javascript(IntPtr view, string script, IntPtr cancellable, IntPtr callback, IntPtr data);
    [DllImport(WebKit)] public static extern void webkit_web_view_evaluate_javascript(IntPtr view, string script, nint length, IntPtr world, IntPtr sourceUri,
                                                                                       IntPtr cancellable, IntPtr callback, IntPtr data);
    [DllImport(WebKit)] public static extern void webkit_web_view_get_snapshot(IntPtr view, int region, int options, IntPtr cancellable, IntPtr callback, IntPtr data);
    [DllImport(WebKit)] public static extern IntPtr webkit_web_view_get_snapshot_finish(IntPtr view, IntPtr result, out IntPtr error);
    [DllImport(WebKit)] public static extern void webkit_settings_set_enable_developer_extras(IntPtr settings, int on);
    [DllImport(WebKit)] public static extern void webkit_settings_set_enable_write_console_messages_to_stdout(IntPtr settings, int on);
    [DllImport(WebKit)] public static extern IntPtr webkit_navigation_policy_decision_get_navigation_action(IntPtr decision);
    [DllImport(WebKit)] public static extern IntPtr webkit_navigation_action_get_request(IntPtr action);
    [DllImport(WebKit)] public static extern IntPtr webkit_uri_request_get_uri(IntPtr request);
    [DllImport(WebKit)] public static extern void webkit_policy_decision_ignore(IntPtr decision);
    [DllImport(WebKit)] public static extern void webkit_policy_decision_use(IntPtr decision);
    [DllImport(Jsc)] public static extern IntPtr jsc_value_to_string(IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    public struct GdkRgba { public double Red, Green, Blue, Alpha; }

    // ---------- AppIndicator (Ayatana's, or the older one)
    [DllImport(Indicator)] public static extern IntPtr app_indicator_new(string id, string iconName, int category);
    [DllImport(Indicator)] public static extern void app_indicator_set_status(IntPtr indicator, int status);
    [DllImport(Indicator)] public static extern void app_indicator_set_icon_theme_path(IntPtr indicator, string path);
    [DllImport(Indicator)] public static extern void app_indicator_set_icon_full(IntPtr indicator, string iconName, string description);
    [DllImport(Indicator)] public static extern void app_indicator_set_menu(IntPtr indicator, IntPtr menu);
    [DllImport(Indicator)] public static extern void app_indicator_set_title(IntPtr indicator, string title);
    [DllImport(Indicator)] public static extern void app_indicator_set_label(IntPtr indicator, string label, string guide);
    [DllImport(Indicator)] public static extern void app_indicator_set_secondary_activate_target(IntPtr indicator, IntPtr menuItem);

    // ---------- libnotify
    [DllImport(Notify)] public static extern int notify_init(string appName);
    [DllImport(Notify)] public static extern IntPtr notify_notification_new(string summary, string body, string? icon);
    [DllImport(Notify)] public static extern void notify_notification_add_action(IntPtr n, string action, string label, IntPtr callback, IntPtr data, IntPtr free);
    [DllImport(Notify)] public static extern void notify_notification_set_timeout(IntPtr n, int ms);
    [DllImport(Notify)] public static extern void notify_notification_set_hint(IntPtr n, string key, IntPtr value);
    [DllImport(Notify)] public static extern int notify_notification_show(IntPtr n, out IntPtr error);
    [DllImport(Notify)] public static extern int notify_get_server_info(out IntPtr name, out IntPtr vendor, out IntPtr version, out IntPtr spec);

    // ---------- cairo
    [DllImport(Cairo)] public static extern IntPtr cairo_image_surface_create(int format, int width, int height);
    [DllImport(Cairo)] public static extern IntPtr cairo_create(IntPtr surface);
    [DllImport(Cairo)] public static extern void cairo_scale(IntPtr cr, double sx, double sy);
    [DllImport(Cairo)] public static extern void cairo_new_path(IntPtr cr);
    [DllImport(Cairo)] public static extern void cairo_new_sub_path(IntPtr cr);
    [DllImport(Cairo)] public static extern void cairo_move_to(IntPtr cr, double x, double y);
    [DllImport(Cairo)] public static extern void cairo_curve_to(IntPtr cr, double x1, double y1, double x2, double y2, double x3, double y3);
    [DllImport(Cairo)] public static extern void cairo_arc(IntPtr cr, double xc, double yc, double radius, double angle1, double angle2);
    [DllImport(Cairo)] public static extern void cairo_close_path(IntPtr cr);
    [DllImport(Cairo)] public static extern void cairo_set_source_rgba(IntPtr cr, double r, double g, double b, double a);
    [DllImport(Cairo)] public static extern void cairo_set_line_width(IntPtr cr, double width);
    [DllImport(Cairo)] public static extern void cairo_fill(IntPtr cr);
    [DllImport(Cairo)] public static extern void cairo_fill_preserve(IntPtr cr);
    [DllImport(Cairo)] public static extern void cairo_stroke(IntPtr cr);
    [DllImport(Cairo)] public static extern void cairo_clip(IntPtr cr);
    [DllImport(Cairo)] public static extern void cairo_reset_clip(IntPtr cr);
    [DllImport(Cairo)] public static extern int cairo_surface_write_to_png(IntPtr surface, string file);
    [DllImport(Cairo)] public static extern void cairo_destroy(IntPtr cr);
    [DllImport(Cairo)] public static extern void cairo_surface_destroy(IntPtr surface);

    // ---------- PulseAudio's simple API (PipeWire answers it too, through pipewire-pulse)
    [StructLayout(LayoutKind.Sequential)]
    public struct PaSampleSpec { public int Format; public uint Rate; public byte Channels; }
    [StructLayout(LayoutKind.Sequential)]
    public struct PaBufferAttr { public uint MaxLength, TLength, PreBuf, MinReq, FragSize; }

    public const int PaStreamPlayback = 1, PaStreamRecord = 2, PaSampleS16Le = 3;

    [DllImport(PulseSimple)] public static extern IntPtr pa_simple_new(string? server, string name, int direction, string? device, string streamName,
                                                                        ref PaSampleSpec spec, IntPtr channelMap, ref PaBufferAttr attr, out int error);
    [DllImport(PulseSimple)] public static extern int pa_simple_read(IntPtr s, byte* data, nuint bytes, out int error);
    [DllImport(PulseSimple)] public static extern int pa_simple_write(IntPtr s, byte* data, nuint bytes, out int error);
    [DllImport(PulseSimple)] public static extern int pa_simple_drain(IntPtr s, out int error);
    [DllImport(PulseSimple)] public static extern int pa_simple_flush(IntPtr s, out int error);
    [DllImport(PulseSimple)] public static extern void pa_simple_free(IntPtr s);
    [DllImport(Pulse)] public static extern IntPtr pa_strerror(int error);

    public static string PulseError(int error)
    {
        try { return Str(pa_strerror(error)) ?? $"error {error}"; }
        catch (DllNotFoundException) { return $"error {error}"; }
    }

    // ---------- X11 (global shortcuts on an X11 session; the idle time)
    [DllImport(X11)] public static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] public static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] public static extern ulong XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] public static extern ulong XStringToKeysym(string name);
    [DllImport(X11)] public static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);
    [DllImport(X11)] public static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, ulong window, int ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(X11)] public static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, ulong window);
    [DllImport(X11)] public static extern int XPending(IntPtr display);
    [DllImport(X11)] public static extern int XNextEvent(IntPtr display, byte* xevent);
    [DllImport(X11)] public static extern int XSync(IntPtr display, int discard);
    [DllImport(X11)] public static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport(X11)] public static extern int XFree(IntPtr data);
    [DllImport(Xss)] public static extern IntPtr XScreenSaverAllocInfo();
    [DllImport(Xss)] public static extern int XScreenSaverQueryInfo(IntPtr display, ulong drawable, IntPtr info);
}
