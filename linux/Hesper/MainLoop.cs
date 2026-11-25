using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Hesper;

/// The UI thread is GTK's main loop: work posted from other threads (await continuations, the engine's
/// progress lines, hesper:// links…) runs there, through one GLib idle callback. As WinForms'
/// SynchronizationContext on Windows: the shared code (AppModel) relies on it.
sealed unsafe class MainLoop : SynchronizationContext
{
    public static readonly MainLoop Instance = new();
    static readonly ConcurrentQueue<(SendOrPostCallback Work, object? State)> queue = new();
    static int scheduled;
    static int uiThread;

    /// Called once, on the thread that runs gtk_main.
    public static void Install()
    {
        uiThread = Environment.CurrentManagedThreadId;
        SetSynchronizationContext(Instance);
    }

    public static bool OnUiThread => Environment.CurrentManagedThreadId == uiThread;

    /// What to do with an exception thrown by posted work (logged and said, as on Windows).
    public static Action<Exception>? Failed;

    public override void Post(SendOrPostCallback d, object? state)
    {
        queue.Enqueue((d, state));
        if (Interlocked.Exchange(ref scheduled, 1) == 0)
            Native.g_idle_add_full(0 /* G_PRIORITY_DEFAULT */, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int>)&Drain, IntPtr.Zero, IntPtr.Zero);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (OnUiThread) { d(state); return; }
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        Post(_ => { try { d(state); } catch (Exception e) { error = e; } finally { done.Set(); } }, null);
        done.Wait();
        if (error != null) throw error;
    }

    public override SynchronizationContext CreateCopy() => this;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static int Drain(IntPtr _)
    {
        Volatile.Write(ref scheduled, 0);
        while (queue.TryDequeue(out var w))
        {
            try { w.Work(w.State); }
            catch (Exception e) { Failed?.Invoke(e); }
        }
        return 0;  // G_SOURCE_REMOVE
    }
}

/// A timer ticking on the UI thread, as WinForms' Timer (which the shared code uses on Windows).
sealed class UiTimer : IDisposable
{
    public int Interval { get; set; } = 100;
    public event EventHandler? Tick;
    Timer? timer;
    int generation;

    public bool Enabled => timer != null;

    public void Start()
    {
        if (timer != null) return;
        var g = ++generation;
        timer = new Timer(_ => MainLoop.Instance.Post(_ => { if (timer != null && g == generation) Tick?.Invoke(this, EventArgs.Empty); }, null),
                          null, Interval, Interval);
    }

    public void Stop()
    {
        generation++;
        timer?.Dispose();
        timer = null;
    }

    public void Dispose() => Stop();
}

/// A few D-Bus calls (GIO's, synchronous, a second at most): the portal's colour scheme, the idle
/// time, the screen lock, the file manager, whether a tray icon can be shown.
static class DBus
{
    const int System = 1, Session = 2;

    /// Calls a method; args in GVariant's text form ("('a', 'b')"), or null for none. The reply's
    /// first value, unwrapped from its variants, as text ("true", "42", "'dark'"); null on failure.
    public static string? Call(bool system, string dest, string path, string iface, string method, string? args = null, int timeoutMs = 1000)
    {
        try
        {
            var bus = Native.g_bus_get_sync(system ? System : Session, IntPtr.Zero, out var err);
            if (bus == IntPtr.Zero) { Native.ErrorText(err); return null; }
            try
            {
                var parameters = IntPtr.Zero;
                if (args != null)
                {
                    parameters = Native.g_variant_parse(IntPtr.Zero, args, IntPtr.Zero, IntPtr.Zero, out var perr);
                    if (parameters == IntPtr.Zero) { Paths.WriteLog($"dbus {method}: {Native.ErrorText(perr)}"); return null; }
                }
                var reply = Native.g_dbus_connection_call_sync(bus, dest, path, iface, method, parameters, IntPtr.Zero, 0, timeoutMs, IntPtr.Zero, out err);
                if (parameters != IntPtr.Zero) Native.g_variant_unref(parameters);  // g_variant_parse's reference isn't floating
                if (reply == IntPtr.Zero) { Native.ErrorText(err); return null; }
                try { return FirstValue(reply); }
                finally { Native.g_variant_unref(reply); }
            }
            finally { Native.g_object_unref(bus); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    static string? FirstValue(IntPtr tuple)
    {
        if (Native.Str(Native.g_variant_get_type_string(tuple)) is not { Length: > 2 }) return "";  // "()": nothing returned
        var v = Native.g_variant_get_child_value(tuple, 0);
        // Properties.Get and the portal wrap their value in one or two variants
        while (Native.Str(Native.g_variant_get_type_string(v)) == "v")
        {
            var inner = Native.g_variant_get_variant(v);
            Native.g_variant_unref(v);
            v = inner;
        }
        try
        {
            return Native.Str(Native.g_variant_get_type_string(v)) switch
            {
                "b" => Native.g_variant_get_boolean(v) != 0 ? "true" : "false",
                "u" => Native.g_variant_get_uint32(v).ToString(),
                "t" => Native.g_variant_get_uint64(v).ToString(),
                _ => "?",
            };
        }
        finally { Native.g_variant_unref(v); }
    }

    /// A string in GVariant's text form.
    public static string Quote(string s)
    {
        var b = new StringBuilder("'");
        foreach (var c in s) b.Append(c is '\'' or '\\' ? "\\" + c : c.ToString());
        return b.Append('\'').ToString();
    }

    /// Someone on the session bus owns this name (a tray host, a file manager…).
    public static bool HasOwner(string name) =>
        Call(false, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", $"({Quote(name)},)") == "true";
}
