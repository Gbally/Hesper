using System.Text.Json.Nodes;

namespace Hesper;

// Shared with the Linux app (linux/Hesper).

/// A calendar meeting, as the Mac's CalendarEvent (meeting.json → "calendar": the engine reads its
/// title and attendees to name speakers).
sealed record CalendarEvent(string Title, DateTime Start, DateTime End, string Organizer, List<(string Name, string Email)> Attendees,
                            bool IsOnline, string? Id, string? SeriesKey)
{
    public JsonObject ToJson() => new()
    {
        ["title"] = Title, ["start"] = Start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), ["end"] = End.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["organizer"] = Organizer, ["isOnlineMeeting"] = IsOnline, ["id"] = Id, ["seriesKey"] = SeriesKey,
        ["attendees"] = new JsonArray(Attendees.Select(a => (JsonNode)new JsonObject { ["name"] = a.Name, ["email"] = a.Email }).ToArray()),
    };
}

/// A calendar as the app uses it (as mac/App/Calendar.swift): the system's events (classic Outlook on
/// Windows, none on Linux) merged with the calendar link's, and the meeting now, today's, the next ones.
abstract class CalendarSource
{
    /// Today's events (and tomorrow morning's), from the last read; [] when off or unavailable.
    public List<CalendarEvent> Events { get; private set; } = [];
    /// From the calendar link (ICS, read by the engine every 15 minutes), merged with the system's.
    public List<CalendarEvent> LinkEvents { get; set; } = [];
    public string? Error { get; private set; }
    public event Action? Changed;

    public abstract void SetEnabled(bool on);
    public abstract void Refresh();

    /// A new read of the system's calendar (on the UI thread).
    protected void Update(List<CalendarEvent> events, string? error)
    {
        Events = events;
        Error = error;
        Changed?.Invoke();
    }

    static bool Real(CalendarEvent e) => e.IsOnline || e.Attendees.Count > 0;

    /// The system's events and the link's, one of each when both have it (same title and start).
    List<CalendarEvent> All => Events.Concat(LinkEvents.Where(l => !Events.Any(e => e.Title == l.Title && e.Start == l.Start))).ToList();

    /// The meeting happening now (or starting within 10 minutes), real meetings first.
    public CalendarEvent? Current(DateTime? at = null)
    {
        var now = at ?? DateTime.Now;
        return All.Where(e => e.End > now && e.Start <= now.AddMinutes(10) && (e.End - e.Start) < TimeSpan.FromHours(6))
                     .OrderByDescending(e => (e.IsOnline ? 3 : 0) + (e.Attendees.Count > 0 ? 2 : 0) - Math.Abs((e.Start - now).TotalHours))
                     .FirstOrDefault();
    }

    /// Today's real meetings (with other people or a call link), in order.
    public List<CalendarEvent> Today() => All.Where(e => e.Start.Date == DateTime.Today && Real(e)).OrderBy(e => e.Start).ToList();

    /// Real meetings starting within the next minutes.
    public List<CalendarEvent> Upcoming(double minutes) =>
        All.Where(e => e.Start >= DateTime.Now && e.Start <= DateTime.Now.AddMinutes(minutes) && Real(e)).OrderBy(e => e.Start).ToList();
}
