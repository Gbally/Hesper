// Views for the 0.7.2 additions: pause / mark / typed notes while recording, the better-model tip,
// the weekly recap, "where was it said?", marked moments, talk time, and their settings.

import AppKit
import SwiftUI

// MARK: - While recording

/// Under the level meters: Pause / Resume, Mark this moment, and a field for your own notes.
struct RecordingTools: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        HStack(spacing: 8) {
            Button { model.togglePause() } label: {
                Label(model.isPaused ? tr("Resume") : tr("Pause"), systemImage: model.isPaused ? "play.fill" : "pause.fill")
                    .frame(maxWidth: .infinity)
            }
            .help(model.isPaused ? tr("Go on recording") : tr("Stop recording for a while; the pause is left out of the notes"))
            Button { model.markMoment() } label: {
                Label(model.marks.isEmpty ? tr("Mark") : tr("Mark · %@", "\(model.marks.count)"), systemImage: "star.fill")
                    .frame(maxWidth: .infinity)
            }
            .disabled(model.isPaused)
            .help(model.settings.markHotkey.map { tr("Mark this moment as important (%@)", $0.display) }
                  ?? tr("Mark this moment as important"))
        }
        .controlSize(.regular)
        VStack(alignment: .leading, spacing: 4) {
            ZStack(alignment: .topLeading) {
                TextEditor(text: $model.liveNotes)
                    .font(.callout)
                    .scrollContentBackground(.hidden)
                    .frame(height: 74)
                    .padding(4)
                    .background(RoundedRectangle(cornerRadius: 6).fill(Color.primary.opacity(0.05)))
                if model.liveNotes.isEmpty {
                    Text("Your notes (optional)…").font(.callout).foregroundColor(.secondary)
                        .padding(.horizontal, 9).padding(.vertical, 4).allowsHitTesting(false)
                }
            }
            Text("Used for the summary. A line starting with [ ] becomes your to-do.")
                .font(.caption2).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }
}

// MARK: - A better model for the notes

struct ModelTipCard: View {
    @EnvironmentObject var model: AppModel
    @AppStorage("modelTipDismissed") private var dismissed = ""

    var body: some View {
        if let best = model.betterNotesModel(dismissed: dismissed) {
            Card {
                Label(tr("Better notes with %@", best.name), systemImage: "sparkles").font(.callout.weight(.medium))
                Text(tr("It's now recommended for this Mac instead of %@: more accurate to-dos, dates and summaries.",
                        model.modelName(model.settings.llmModel ?? "")))
                    .font(.caption).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Button(model.installedModels[best.repo] != nil ? tr("Switch") : tr("Download and switch (%@ GB)", String(format: "%.1f", best.sizeGB))) {
                        model.useModel(best, speech: false)
                        dismissed = best.repo
                    }
                    .buttonStyle(.borderedProminent)
                    Button("Not now") { dismissed = best.repo }
                }
                .controlSize(.small)
            }
        }
    }
}

// MARK: - The weekly recap

struct WeekView: View {
    @EnvironmentObject var model: AppModel
    @Binding var route: Route
    var inWindow = false

    var body: some View {
        let w = model.weekSummary()
        VStack(spacing: 0) {
            Header(title: "This week", back: inWindow ? nil : { route = .home }) { EmptyView() }
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Text(w.interval.start.local(date: .long, time: .omitted) + " – "
                         + w.interval.end.addingTimeInterval(-1).local(date: .long, time: .omitted))
                        .font(.caption).foregroundColor(.secondary)
                    LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 8) {
                        stat("\(w.meetings.count)", w.meetings.count == 1 ? tr("meeting") : tr("meetings"))
                        stat(w.minutes >= 60 ? "\(w.minutes / 60) h \(String(format: "%02d", w.minutes % 60))" : "\(w.minutes) min",
                             tr("in meetings"))
                        stat("\(w.mineOpen.count)", tr("to-dos for you"))
                        stat("\(w.mineDone)", tr("done"))
                    }
                    if !w.overdue.isEmpty {
                        SectionTitle(text: "Overdue")
                        ForEach(w.overdue) { todoRow($0) }
                    }
                    SectionTitle(text: "Your open to-dos")
                    if w.mineOpen.isEmpty {
                        Text("Nothing open for you from this week's meetings.").font(.callout).foregroundColor(.secondary)
                    }
                    ForEach(w.mineOpen) { todoRow($0) }
                    if !w.decisions.isEmpty {
                        SectionTitle(text: "Decisions")
                        ForEach(w.decisions, id: \.meeting.id) { d in
                            Text(d.meeting.title).font(.caption.weight(.semibold)).foregroundColor(.secondary)
                            ForEach(d.items, id: \.self) { item in
                                HStack(alignment: .top, spacing: 6) {
                                    Text("•").foregroundColor(.secondary)
                                    Text(item).font(.callout).fixedSize(horizontal: false, vertical: true)
                                }
                            }
                        }
                    }
                    SectionTitle(text: "Meetings")
                    ForEach(w.meetings) { m in
                        Button { open(m.id) } label: {
                            HStack {
                                VStack(alignment: .leading, spacing: 1) {
                                    Text(m.title).font(.callout).lineLimit(1)
                                    Text(m.date.local(date: .abbreviated, time: .shortened)
                                         + (m.duration.map { " · " + formatDuration($0) } ?? ""))
                                        .font(.caption).foregroundColor(.secondary)
                                }
                                Spacer()
                                Image(systemName: "chevron.right").font(.caption).foregroundColor(.secondary)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(14)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    func stat(_ value: String, _ label: String) -> some View {
        VStack(spacing: 2) {
            Text(value).font(.title2.weight(.semibold)).monospacedDigit()
            Text(label).font(.caption).foregroundColor(.secondary)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.05)))
    }

    func todoRow(_ t: AppModel.TodoRef) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Button { model.toggleDone(t.meeting.id, t.index) } label: {
                Image(systemName: t.done ? "checkmark.circle.fill" : "circle").foregroundColor(t.done ? .green : .secondary)
            }
            .buttonStyle(.plain)
            Button { open(t.meeting.id) } label: {
                VStack(alignment: .leading, spacing: 2) {
                    Text(t.item.task).font(.callout).fixedSize(horizontal: false, vertical: true)
                    HStack(spacing: 6) {
                        Text(t.meeting.title).font(.caption).foregroundColor(.secondary).lineLimit(1)
                        DueBadge(item: t.item, done: t.done)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        }
    }

    func open(_ id: Meeting.ID) {
        if inWindow { Windows.showMeeting(id, model: model) } else { route = .meeting(id) }
    }
}

// MARK: - In a meeting: where was it said, marked moments, your notes, talk time

/// Under a to-do: the lines it was noted from, and ▶︎ to hear that moment (while the audio is kept).
struct WhereSaid: View {
    var meeting: Meeting
    var at: String
    @ObservedObject private var player = MomentPlayer.shared

    var body: some View {
        if let s = seconds(from: at) {
            let lines = meeting.lines(around: s)
            VStack(alignment: .leading, spacing: 4) {
                ForEach(lines, id: \.self) { l in
                    (Text(l.speaker + ": ").font(.caption.weight(.semibold)) + Text(l.text).font(.caption))
                        .foregroundColor(l == lines.last ? .primary : .secondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
                }
                HStack(spacing: 10) {
                    Text(at).font(.caption2).foregroundColor(.secondary).monospacedDigit()
                    if meeting.hasPlayableAudio {
                        let key = "\(meeting.id)@\(at)"
                        Button {
                            player.toggle(key, folder: meeting.folder, from: lines.first?.start ?? s)
                        } label: {
                            Label(player.playing == key ? tr("Stop") : tr("Listen"),
                                  systemImage: player.playing == key ? "stop.fill" : "play.fill").font(.caption)
                        }
                        .buttonStyle(.borderless)
                    }
                }
            }
            .padding(8)
            .background(RoundedRectangle(cornerRadius: 8).fill(Brand.accent.opacity(0.07)))
        }
    }
}

struct HighlightsSection: View {
    var meeting: Meeting
    var highlights: [MeetingNotes.Highlight]
    @ObservedObject private var player = MomentPlayer.shared

    var body: some View {
        if !highlights.isEmpty {
            SectionTitle(text: "Marked moments")
            ForEach(highlights, id: \.self) { h in
                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: 8) {
                        Image(systemName: "star.fill").foregroundColor(Brand.accent).font(.caption)
                        Text(h.at).font(.caption.weight(.semibold)).monospacedDigit()
                        Spacer()
                        if meeting.hasPlayableAudio, let s = h.seconds ?? seconds(from: h.at) {
                            let key = "\(meeting.id)★\(h.at)"
                            let from = h.lines.first.flatMap { $0.at.flatMap(seconds(from:)) } ?? max(0, s - 30)
                            Button {
                                player.toggle(key, folder: meeting.folder, from: from, length: max(15, s - from + 8))
                            } label: {
                                Label(player.playing == key ? tr("Stop") : tr("Listen"),
                                      systemImage: player.playing == key ? "stop.fill" : "play.fill").font(.caption)
                            }
                            .buttonStyle(.borderless)
                        }
                    }
                    if h.lines.isEmpty { Text("Nothing was said just before.").font(.caption).foregroundColor(.secondary) }
                    ForEach(h.lines, id: \.self) { l in
                        (Text(l.speaker + ": ").font(.caption.weight(.semibold)) + Text(l.text).font(.caption))
                            .fixedSize(horizontal: false, vertical: true)
                            .textSelection(.enabled)
                    }
                }
                .padding(8)
                .background(RoundedRectangle(cornerRadius: 8).fill(Brand.accent.opacity(0.07)))
            }
        }
    }
}

struct MyNotesSection: View {
    var text: String

    var body: some View {
        SectionTitle(text: "My notes")
        Text(text).font(.callout).fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
    }
}

/// Who spoke how long: one bar per person.
struct TalkTimeSection: View {
    var meeting: Meeting

    var body: some View {
        let times = meeting.talkTime
        let total = times.map(\.seconds).reduce(0, +)
        if times.count > 1, total > 0 {
            SectionTitle(text: "Talk time")
            VStack(alignment: .leading, spacing: 5) {
                ForEach(times, id: \.speaker) { t in
                    HStack(spacing: 8) {
                        Text(t.speaker).font(.caption).lineLimit(1).frame(width: 96, alignment: .leading)
                        GeometryReader { geo in
                            Capsule().fill(Brand.accent.opacity(0.75))
                                .frame(width: max(3, geo.size.width * t.seconds / (times.first?.seconds ?? total)))
                        }
                        .frame(height: 8)
                        Text(verbatim: "\(Int((t.seconds / total * 100).rounded())) %").font(.caption).foregroundColor(.secondary)
                            .monospacedDigit().frame(width: 38, alignment: .trailing)
                    }
                }
            }
        }
    }
}

// MARK: - Settings

/// Settings → Recording → Calendar: a calendar link (ICS) besides the Mac's calendar.
struct CalendarLinkRow: View {
    @EnvironmentObject var model: AppModel
    @State private var draft = ""
    @State private var checking = false

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                TextField("Calendar link (ICS)", text: $draft, prompt: Text(verbatim: "https://calendar.google.com/…/basic.ics"))
                    .onSubmit(save)
                Button(checking ? tr("Checking…") : tr("Use")) { save() }
                    .disabled(checking || draft.trimmingCharacters(in: .whitespaces) == model.settings.calendarLink)
                if !model.settings.calendarLink.isEmpty {
                    Button("Remove") { draft = ""; save() }
                }
            }
            switch model.calendarLinkStatus {
            case .ok(let n)?:
                Text(n == 1 ? tr("Read: 1 event from yesterday to the day after tomorrow.")
                     : tr("Read: %@ events from yesterday to the day after tomorrow.", "\(n)"))
                    .font(.caption).foregroundColor(.green)
            case .failed(let why)?:
                Text(tr("Couldn't read it: %@", why)).font(.caption).foregroundColor(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            case nil: EmptyView()
            }
            Text("For a calendar not in the Calendar app: Google Calendar (Settings → your calendar → “Secret address in iCal format”), the new Outlook (Settings → Calendar → Shared calendars → Publish), iCloud… Only this link is read, every 15 minutes.")
                .font(.caption2).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .onAppear { draft = model.settings.calendarLink }
    }

    func save() {
        model.settings.calendarLink = draft.trimmingCharacters(in: .whitespaces)
        checking = true
        Task {
            await model.refreshCalendarLink(force: true)
            checking = false
        }
    }
}

/// Settings → General: hesper:// links for Shortcuts, Raycast, Alfred, a Stream Deck…
struct LinksSection: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        Section("Shortcuts and automation") {
            Toggle("Allow hesper:// links to control recording", isOn: $model.settings.allowLinks)
            Caption("Use them in the Shortcuts app (“Open URL”), Raycast, Alfred or a Stream Deck. A recording started by a link is always announced.")
            VStack(alignment: .leading, spacing: 3) {
                ForEach(["record", "stop", "pause", "resume", "mark", "note?text=…", "todos", "week"], id: \.self) { a in
                    Text(verbatim: "hesper://\(a)").font(.system(.caption, design: .monospaced)).textSelection(.enabled)
                }
            }
            .disabled(!model.settings.allowLinks)
        }
    }
}
