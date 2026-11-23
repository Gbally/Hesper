// 0.7.2 additions: the calendar link (ICS), the weekly recap, hesper:// links, the "better model"
// tip, talk time, and "where was it said?" (transcript lines + the audio from that moment).

import AppKit
import AVFoundation
import Foundation
import UserNotifications

// MARK: - Calendar link

extension AppModel {
    /// Reads the calendar link (at most every 15 min, or now when `force`): Google's secret address,
    /// the new Outlook's published calendar… The engine fetches and expands it (`hesper calendar`).
    func refreshCalendarLink(force: Bool = false) async {
        let link = settings.calendarLink.trimmingCharacters(in: .whitespaces)
        guard !link.isEmpty, settings.useCalendar else {
            CalendarService.linkEvents = []
            calendarLinkStatus = nil
            return
        }
        guard phase == .ready, force || Date().timeIntervalSince(calendarLinkFetched) > 15 * 60 else { return }
        calendarLinkFetched = Date()
        var reply: [String: Any]?
        _ = await Engine.hesper(["calendar", link, "--days", "2"]) { line in
            if line.hasPrefix("@@result "), let data = line.dropFirst(9).data(using: .utf8) {
                reply = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
            }
        }
        guard let reply else { calendarLinkStatus = .failed(tr("Couldn't read the calendar link.")); return }
        if let error = reply["error"] as? String { calendarLinkStatus = .failed(error); return }
        let iso = ISO8601DateFormatter()
        let me = settings.myName.lowercased()
        let events: [CalendarEvent] = (reply["events"] as? [[String: Any]] ?? []).compactMap { e in
            guard let s = (e["start"] as? String).flatMap(iso.date), let f = (e["end"] as? String).flatMap(iso.date) else { return nil }
            let people = (e["attendees"] as? [[String: String]] ?? [])
                .map { Attendee(name: $0["name"] ?? "", email: $0["email"] ?? "") }
                .filter { me.isEmpty || $0.name.lowercased() != me }  // you're not one of "the others"
            return CalendarEvent(title: (e["title"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? tr("Meeting"),
                                 start: s, end: f, organizer: e["organizer"] as? String ?? "", attendees: people,
                                 isOnlineMeeting: e["online"] as? Bool ?? false, id: e["id"] as? String,
                                 seriesKey: e["series"] as? String)
        }
        CalendarService.linkEvents = events
        calendarLinkStatus = .ok(events.count)
        refreshCalendar()
    }

    enum CalendarLinkStatus: Equatable {
        case ok(Int)          // events found from yesterday to 2 days ahead
        case failed(String)
    }
}

// MARK: - The weekly recap

struct WeekSummary {
    var interval: DateInterval
    var meetings: [Meeting]
    var minutes: Int
    var mineOpen: [AppModel.TodoRef]     // my to-dos from this week's meetings, still open (not overdue: listed apart)
    var mineDone: Int
    var overdue: [AppModel.TodoRef]      // any meeting's: mine, open, due before today
    var decisions: [(meeting: Meeting, items: [String])]
}

extension AppModel {
    func weekSummary(of day: Date = Date()) -> WeekSummary {
        let cal = Calendar.current
        let week = cal.dateInterval(of: .weekOfYear, for: day) ?? DateInterval(start: day, duration: 7 * 86400)
        let these = meetings.filter { week.contains($0.date) && $0.notes != nil }
        let ids = Set(these.map(\.id))
        let mine = todos(mineOnly: true)
        let today = cal.startOfDay(for: Date())
        return WeekSummary(
            interval: week, meetings: these,
            minutes: Int(these.compactMap(\.duration).reduce(0, +) / 60),
            mineOpen: mine.filter { t in ids.contains(t.meeting.id) && !t.done && !(t.item.dueDay.map { $0 < today } ?? false) }
                .sorted { ($0.item.dueDay ?? .distantFuture) < ($1.item.dueDay ?? .distantFuture) },
            mineDone: mine.filter { ids.contains($0.meeting.id) && $0.done }.count,
            overdue: mine.filter { !$0.done && ($0.item.dueDay.map { $0 < today } ?? false) },
            decisions: these.compactMap { m in m.notes.map { (m, $0.decisions) } }.filter { !$0.items.isEmpty })
    }

    /// Friday from 16:00 (or the first time the Mac is on after that, until Sunday): one
    /// notification with the week's numbers; clicking it opens the recap.
    func checkWeeklyRecap(now: Date = Date()) {
        guard settings.weeklyRecap, phase == .ready else { return }
        let cal = Calendar.current
        let weekday = cal.component(.weekday, from: now)  // 1 = Sunday … 6 = Friday, 7 = Saturday
        guard (weekday == 6 && cal.component(.hour, from: now) >= 16) || weekday == 7 || weekday == 1 else { return }
        let key = "\(cal.component(.yearForWeekOfYear, from: now))-\(cal.component(.weekOfYear, from: now))"
        guard UserDefaults.standard.string(forKey: "weeklyRecapSent") != key else { return }
        UserDefaults.standard.set(key, forKey: "weeklyRecapSent")
        let w = weekSummary(of: now)
        guard !w.meetings.isEmpty else { return }
        let content = UNMutableNotificationContent()
        content.title = w.meetings.count == 1 ? tr("Your week: 1 meeting") : tr("Your week: %@ meetings", "\(w.meetings.count)")
        var parts = [w.mineOpen.count == 1 ? tr("1 to-do for you") : tr("%@ to-dos for you", "\(w.mineOpen.count)")]
        if !w.overdue.isEmpty { parts.append(tr("%@ overdue", "\(w.overdue.count)")) }
        if w.mineDone > 0 { parts.append(tr("%@ done", "\(w.mineDone)")) }
        content.body = parts.joined(separator: " · ") + ". " + tr("Click to see the recap.")
        content.sound = .default
        content.userInfo = ["meeting": "week"]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "week-\(key)", content: content, trigger: nil))
    }
}

// MARK: - hesper:// links (Shortcuts, Raycast, Alfred, Stream Deck, a terminal…)

extension AppModel {
    /// hesper://record · stop · toggle · pause · resume · mark · note?text=… · open · todos · week
    func handleLink(_ url: URL) {
        guard url.scheme?.lowercased() == "hesper" else { return }
        let action = (url.host ?? url.path).lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        let harmless = ["open", "todos", "week"]
        guard settings.allowLinks || harmless.contains(action) else {
            show(tr("hesper:// links are turned off (Settings → General)."))
            return
        }
        switch action {
        case "record", "start":
            guard !isRecording else { return }
            startRecording(title: URLComponents(url: url, resolvingAgainstBaseURL: false)?
                .queryItems?.first { $0.name == "title" }?.value ?? "")
            // Anything on this Mac can open a link: a recording started by one is always announced.
            notify(title: tr("Recording started"), body: tr("Started by a hesper:// link (Shortcuts, Raycast…)."))
        case "stop": stopRecording()
        case "toggle": toggleRecording()
        case "pause": if isRecording && !isPaused { togglePause() }
        case "resume": if isPaused { togglePause() }
        case "mark": markMoment()
        case "note":
            let text = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first { $0.name == "text" }?.value ?? ""
            guard isRecording, !text.isEmpty else { return }
            liveNotes += (liveNotes.isEmpty || liveNotes.hasSuffix("\n") ? "" : "\n") + text
        case "open": Windows.showMain(self)
        case "todos": pendingRoute = .todos; Windows.showMain(self)
        case "week": Windows.showWeek(self)
        default: show(tr("Unknown link: %@", url.absoluteString))
        }
    }
}

// MARK: - A better notes model than the one picked

extension AppModel {
    /// The catalog now recommends a better model for this Mac than the one chosen earlier (e.g.
    /// Qwen3.5 4B over Qwen3 4B): offered once, until dismissed.
    func betterNotesModel(dismissed: String) -> CatalogModel? {
        guard let current = settings.llmModel, phase == .ready, !isRecording,
              let picked = catalog.notes.first(where: { $0.repo == current }),
              let best = ModelCatalog.recommended(catalog.notes, for: .current),
              best.repo != current, best.quality > picked.quality, dismissed != best.repo else { return nil }
        return best
    }
}

// MARK: - Transcript helpers: talk time, the lines around a moment

struct TranscriptLine: Hashable {
    var start: Double
    var speaker: String
    var text: String
}

extension Meeting {
    var transcriptLines: [TranscriptLine] {
        guard let data = try? Data(contentsOf: url("transcript.json")),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let utts = json["utterances"] as? [[String: Any]] else { return [] }
        return utts.compactMap { u in
            guard let s = u["start"] as? Double, let spk = u["speaker"] as? String, let text = u["text"] as? String else { return nil }
            return TranscriptLine(start: s, speaker: spk, text: text)
        }
    }

    /// Seconds each person spoke, most first.
    var talkTime: [(speaker: String, seconds: Double)] {
        guard let data = try? Data(contentsOf: url("transcript.json")),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let utts = json["utterances"] as? [[String: Any]] else { return [] }
        var total: [String: Double] = [:]
        for u in utts {
            guard let spk = u["speaker"] as? String, let s = u["start"] as? Double, let e = u["end"] as? Double, e > s else { continue }
            total[spk, default: 0] += e - s
        }
        return total.map { ($0.key, $0.value) }.sorted { $0.seconds > $1.seconds }
    }

    /// The line said at `seconds` (the one a to-do was noted from) with the two before it.
    func lines(around seconds: Double, before: Int = 2) -> [TranscriptLine] {
        let all = transcriptLines
        guard let i = all.lastIndex(where: { $0.start <= seconds + 0.5 }) else { return Array(all.prefix(1)) }
        return Array(all[max(0, i - before)...i])
    }

    var hasPlayableAudio: Bool {
        ["mic.caf", "system.caf"].contains { FileManager.default.fileExists(atPath: url($0).path) }
    }
}

/// "00:12:30" / "12:30" → seconds.
func seconds(from stamp: String) -> Double? {
    let parts = stamp.split(separator: ":").compactMap { Double($0) }
    guard parts.count >= 2, parts.count == stamp.split(separator: ":").count else { return nil }
    return parts.reduce(0) { $0 * 60 + $1 }
}

// MARK: - Playing the audio from a moment (both tracks together)

final class MomentPlayer: NSObject, ObservableObject {
    static let shared = MomentPlayer()
    @Published private(set) var playing: String?   // which moment (a key chosen by the view)
    private var players: [AVAudioPlayer] = []
    private var stopTimer: Timer?

    /// Plays both tracks (your mic + the meeting) from a little before `seconds`, for `length` seconds.
    func toggle(_ key: String, folder: URL, from seconds: Double, length: Double = 25) {
        if playing == key { stop(); return }
        stop()
        let start = max(0, seconds - 3)
        players = ["mic.caf", "system.caf"].compactMap { try? AVAudioPlayer(contentsOf: folder.appendingPathComponent($0)) }
        guard let first = players.first else { return }
        let at = first.deviceCurrentTime + 0.1
        for p in players where start < p.duration {
            p.currentTime = start
            p.prepareToPlay()
            p.play(atTime: at)
        }
        playing = key
        stopTimer = Timer.scheduledTimer(withTimeInterval: length, repeats: false) { [weak self] _ in self?.stop() }
    }

    func stop() {
        stopTimer?.invalidate()
        players.forEach { $0.stop() }
        players = []
        playing = nil
    }
}
