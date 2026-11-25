using System.Runtime.InteropServices;

namespace Hesper;

/// The user's calendar in classic Outlook (its COM object model: the new Outlook has none on the
/// PC). Read on a thread of its own every minute, so Outlook being slow never freezes Hesper.
/// As mac/App/Calendar.swift: the meeting now, today's real meetings, the ones about to start.
sealed class OutlookCalendar : CalendarSource, IDisposable
{
    /// Classic Outlook is installed and has a mail profile (installed but never set up, it can't be read).
    public static bool Available =>
        Type.GetTypeFromProgID("Outlook.Application") != null
        && (Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office\16.0\Outlook\Profiles")?.SubKeyCount ?? 0) > 0;

    /// Installed but with no account: say so in Settings.
    public static bool Installed => Type.GetTypeFromProgID("Outlook.Application") != null;

    readonly Thread thread;
    readonly AutoResetEvent wake = new(false);
    volatile bool enabled, stopping;
    readonly SynchronizationContext? ui = SynchronizationContext.Current;

    public OutlookCalendar(bool on)
    {
        enabled = on;
        thread = new Thread(Loop) { IsBackground = true, Name = "Outlook calendar" };
        thread.SetApartmentState(ApartmentState.STA);  // Outlook's objects want a single-threaded apartment
        thread.Start();
    }

    public override void SetEnabled(bool on)
    {
        enabled = on;
        wake.Set();
    }

    public override void Refresh() => wake.Set();

    void Loop()
    {
        while (!stopping)
        {
            List<CalendarEvent> found = [];
            string? error = null;
            if (enabled && Available)
            {
                try { found = Read(); }
                catch (Exception e) when (e is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    error = e.Message;
                    Paths.WriteLog("calendar: " + e.Message);
                }
            }
            ui?.Post(_ => Update(found, error), null);
            wake.WaitOne(TimeSpan.FromMinutes(1));
        }
    }

    static readonly string[] CallLinks = ["teams.microsoft.com", "zoom.us", "webex.com", "meet.google.com"];

    /// The events from midnight today to midnight tomorrow, recurrences included.
    public static List<CalendarEvent> Read()
    {
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Outlook.Application")!)!;
        dynamic ns = app.GetNamespace("MAPI");
        dynamic folder = ns.GetDefaultFolder(9);  // olFolderCalendar
        dynamic items = folder.Items;
        items.IncludeRecurrences = true;
        items.Sort("[Start]");
        var from = DateTime.Today;
        var to = from.AddDays(2);
        string Fmt(DateTime d) => d.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        dynamic found = items.Restrict($"[Start] < '{Fmt(to)}' AND [End] > '{Fmt(from)}'");
        var me = ((string?)ns.CurrentUser?.Name ?? "").Trim();
        var events = new List<CalendarEvent>();
        foreach (dynamic e in found)
        {
            if ((bool)e.AllDayEvent || (int)e.MeetingStatus is 5 or 7) continue;  // all day, canceled
            var people = new List<(string, string)>();
            foreach (dynamic r in e.Recipients)
            {
                var name = (string)(r.Name ?? "");
                if (name.Length == 0 || name.Equals(me, StringComparison.OrdinalIgnoreCase) || (int)r.Type == 3) continue;  // olResource
                string email = "";
                try { email = (string)(r.Address ?? ""); } catch (COMException) { }
                people.Add((name, email.Contains('@') ? email : ""));
            }
            var text = $"{e.Location} {e.Body}".ToLowerInvariant();
            events.Add(new CalendarEvent((string)(e.Subject ?? "Meeting"), (DateTime)e.Start, (DateTime)e.End, (string)(e.Organizer ?? ""), people,
                                         CallLinks.Any(text.Contains), $"{e.GlobalAppointmentID}/{((DateTime)e.Start):s}",
                                         (string?)e.GlobalAppointmentID));
            if (events.Count > 60) break;
        }
        return events;
    }

    public void Dispose()
    {
        stopping = true;
        wake.Set();
    }
}
