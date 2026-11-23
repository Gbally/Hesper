// Calendar (meeting titles + attendees) and Reminders (export to-dos), via macOS EventKit.
// Outlook / Exchange events show up here when the work account is added to the Calendar app
// (System Settings → Internet Accounts → Microsoft Exchange).

import EventKit
import Foundation

struct Attendee: Codable, Hashable {
    var name: String
    var email: String
}

struct CalendarEvent: Codable, Equatable {
    var title: String
    var start: Date
    var end: Date
    var organizer: String
    var attendees: [Attendee]
    var isOnlineMeeting: Bool
    var id: String?          // this occurrence
    var seriesKey: String?   // shared by all occurrences of a recurring meeting

    var timeRange: String {
        "\(start.local(date: .omitted, time: .shortened))–\(end.local(date: .omitted, time: .shortened))"
    }
}

final class CalendarService {
    static let shared = CalendarService()
    private let store = EKEventStore()

    // .authorized has the same raw value as macOS 14's .fullAccess.
    var eventsAllowed: Bool { EKEventStore.authorizationStatus(for: .event) == .authorized }
    var eventsUndecided: Bool { EKEventStore.authorizationStatus(for: .event) == .notDetermined }

    func requestEvents() async -> Bool {
        await withCheckedContinuation { c in store.requestAccess(to: .event) { ok, _ in c.resume(returning: ok) } }
    }

    func requestReminders() async -> Bool {
        if EKEventStore.authorizationStatus(for: .reminder) == .authorized { return true }
        return await withCheckedContinuation { c in store.requestAccess(to: .reminder) { ok, _ in c.resume(returning: ok) } }
    }

    /// Events from the calendar link (Settings → Recording → Calendar), refreshed by the app.
    @MainActor static var linkEvents: [CalendarEvent] = []

    /// Timed events overlapping from…to: the Mac's calendars (when allowed) and the calendar link,
    /// an event in both counted once.
    @MainActor private func events(from: Date, to: Date) -> [CalendarEvent] {
        var out: [CalendarEvent] = []
        if eventsAllowed {
            out = store.events(matching: store.predicateForEvents(withStart: from, end: to, calendars: nil))
                .filter { !$0.isAllDay && $0.status != .canceled }
                .map(Self.convert)
        }
        let seen = Set(out.map { "\($0.title.lowercased())|\(Int($0.start.timeIntervalSince1970))" })
        out += Self.linkEvents.filter {
            $0.end > from && $0.start < to && !seen.contains("\($0.title.lowercased())|\(Int($0.start.timeIntervalSince1970))")
        }
        return out
    }

    /// A real meeting: with other people, or a Teams/Zoom/Meet link.
    private static func isMeeting(_ e: CalendarEvent) -> Bool { e.isOnlineMeeting || !e.attendees.isEmpty }

    /// The meeting happening now (or starting within 10 minutes), preferring real meetings
    /// (with attendees or a Teams/Zoom link) over personal blocks.
    @MainActor func currentMeeting(at now: Date = Date()) -> CalendarEvent? {
        let candidates = events(from: now.addingTimeInterval(-6 * 3600), to: now.addingTimeInterval(600)).filter {
            $0.end > now && $0.start <= now.addingTimeInterval(600) && $0.end.timeIntervalSince($0.start) < 6 * 3600
        }
        return candidates.max { score($0, now) < score($1, now) }
    }

    /// Today's real meetings (with other people or a call link), in order.
    @MainActor func todayMeetings(_ now: Date = Date()) -> [CalendarEvent] {
        let cal = Calendar.current
        let start = cal.startOfDay(for: now)
        let end = cal.date(byAdding: .day, value: 1, to: start)!
        return events(from: start, to: end).filter { $0.start >= start && Self.isMeeting($0) }.sorted { $0.start < $1.start }
    }

    /// Real meetings (with other people or a call link) starting within the next `minutes`.
    @MainActor func upcomingMeetings(within minutes: Double, from now: Date = Date()) -> [CalendarEvent] {
        events(from: now, to: now.addingTimeInterval(minutes * 60))
            .filter { $0.start >= now && Self.isMeeting($0) }
            .sorted { $0.start < $1.start }
    }

    private func score(_ e: CalendarEvent, _ now: Date) -> Double {
        var s = 0.0
        if e.isOnlineMeeting { s += 3 }
        if !e.attendees.isEmpty { s += 2 }
        s -= abs(e.start.timeIntervalSince(now)) / 3600 // closest start wins ties
        return s
    }

    private static func isOnline(_ e: EKEvent) -> Bool {
        let text = [e.location, e.notes, e.url?.absoluteString].compactMap { $0 }.joined(separator: " ").lowercased()
        return ["teams.microsoft.com", "zoom.us", "webex.com", "meet.google.com"].contains { text.contains($0) }
    }

    private static func convert(_ e: EKEvent) -> CalendarEvent {
        func attendee(_ p: EKParticipant) -> Attendee {
            let email = p.url.absoluteString.replacingOccurrences(of: "mailto:", with: "", options: .caseInsensitive)
            let name = p.name ?? email.components(separatedBy: "@").first ?? email
            return Attendee(name: name, email: email.contains("@") ? email : "")
        }
        let people = (e.attendees ?? []).filter {
            $0.participantType != .room && $0.participantType != .resource && !$0.isCurrentUser
        }
        return CalendarEvent(title: e.title ?? tr("Meeting"), start: e.startDate, end: e.endDate,
                             organizer: e.organizer?.name ?? "", attendees: people.map(attendee),
                             isOnlineMeeting: isOnline(e), id: e.calendarItemIdentifier,
                             seriesKey: e.calendarItemExternalIdentifier)
    }

    /// Adds to-dos to the default Reminders list. Returns how many were added.
    func addReminders(_ items: [ActionItem], meetingTitle: String) async -> Int? {
        guard await requestReminders(), let list = store.defaultCalendarForNewReminders() else { return nil }
        var added = 0
        for item in items {
            let r = EKReminder(eventStore: store)
            r.calendar = list
            r.title = item.task
            r.notes = [tr("From meeting: %@", meetingTitle), item.due.isEmpty ? nil : tr("Due: %@", item.due)]
                .compactMap { $0 }.joined(separator: "\n")
            if (try? store.save(r, commit: false)) != nil { added += 1 }
        }
        try? store.commit()
        return added
    }
}
