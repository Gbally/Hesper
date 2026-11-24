import SwiftUI
import UserNotifications

@main
struct HesperApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    var body: some Scene {
        MenuBarExtra {
            RootView().environmentObject(delegate.model).tint(Brand.accent)
        } label: {
            MenuBarLabel().environmentObject(delegate.model)
        }
        .menuBarExtraStyle(.window)
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, UNUserNotificationCenterDelegate {
    let model = AppModel()

    func applicationWillFinishLaunching(_ notification: Notification) {
        UNUserNotificationCenter.current().delegate = self
    }

    /// `open -a Hesper --args --settings models` opens Settings on that tab (for testing).
    func applicationDidFinishLaunching(_ notification: Notification) {
        let args = CommandLine.arguments
        func value(_ flag: String) -> String? { args.firstIndex(of: flag).flatMap { $0 + 1 < args.count ? args[$0 + 1] : nil } }
        if args.contains("--light") { NSApp.appearance = NSAppearance(named: .aqua) }  // screenshots
        if let i = args.firstIndex(of: "--settings") {
            let tab = i + 1 < args.count ? SettingsTab(rawValue: args[i + 1]) : nil
            Windows.showSettings(model, tab: tab)
        }
        if args.contains("--preview") { Windows.showPreview(model) }
        // Left in its own window last time: open it again (not in demo / screenshot runs).
        if Windows.wasDetached, AppModel.demo == nil, !model.shouldWelcome { Windows.showMain(model) }
        if args.contains("--window") { Windows.showMain(model) }  // testing
        if args.contains("--week") { Windows.showWeek(model) }  // testing
        // The demo video: a recording in progress (no audio), a question asked in the window.
        if AppModel.demoFlag("--fake-recording") {
            model.demoRecording = true
            model.elapsed = value("--fake-elapsed").flatMap(Double.init) ?? 1_234
            model.micLevel = 0.35
            model.systemLevel = 0.6
            model.marks = Array([312.0, 845, 1102].prefix(value("--fake-marks").flatMap(Int.init) ?? 2))
            model.liveNotes = value("--fake-notes")?.replacingOccurrences(of: "\\n", with: "\n")
                ?? "Budget cap 80k is the real constraint\n[ ] ask Finance for a phased invoice"
            if let toast = value("--fake-toast") { model.toast = toast }
        }
        if AppModel.demo != nil, let r = value("--route") { model.pendingRoute = ["week": .week, "todos": .todos][r] }
        if AppModel.demo != nil, let q = value("--ask-only") {  // the Ask page before the answer
            model.askQuestion = q
            model.pendingRoute = .ask
        }
        if AppModel.demo != nil, let i = args.firstIndex(of: "--ask-result"), i + 2 < args.count,
           let data = try? Data(contentsOf: URL(fileURLWithPath: args[i + 2])) {
            model.askQuestion = args[i + 1]
            model.askResult = try? JSONDecoder().decode(AskResult.self, from: data)
            model.pendingRoute = .ask
        }
        // Testing: --welcome [page]
        if let i = args.firstIndex(of: "--welcome") {
            Windows.showWelcome(model, step: i + 1 < args.count ? Int(args[i + 1]) ?? 0 : 0)
        } else if model.shouldWelcome {
            Windows.showWelcome(model)
        }
        // Testing: switch language while running, e.g. --switch-language fr
        if let i = args.firstIndex(of: "--switch-language"), i + 1 < args.count {
            let code = args[i + 1]
            DispatchQueue.main.asyncAfter(deadline: .now() + 3) { self.model.setLanguage(code) }
        }
        if let i = args.firstIndex(of: "--meeting"), i + 1 < args.count {
            let id = args[i + 1]
            DispatchQueue.main.asyncAfter(deadline: .now() + 2) { Windows.showMeeting(id, model: self.model) }
        }
        // Testing: --snapshot DIR saves each open window as a PNG (the app draws its own views: no
        // screen-recording permission), plus the menu-bar icons, then quits.
        if let i = args.firstIndex(of: "--snapshot"), i + 1 < args.count {
            let dir = URL(fileURLWithPath: args[i + 1])
            let delay = args.firstIndex(of: "--snapshot-delay").flatMap { $0 + 1 < args.count ? Double(args[$0 + 1]) : nil } ?? 4
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) {
                try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                func save(_ rep: NSBitmapImageRep?, _ name: String) {
                    if let data = rep?.representation(using: .png, properties: [:]) { try? data.write(to: dir.appendingPathComponent(name)) }
                }
                for (n, w) in NSApp.windows.enumerated() where w.isVisible {
                    guard let v = w.contentView, let rep = v.bitmapImageRepForCachingDisplay(in: v.bounds) else { continue }
                    v.cacheDisplay(in: v.bounds, to: rep)
                    save(rep, "window-\(n)-\(w.title.replacingOccurrences(of: "/", with: "-")).png")
                    // A scrolling page, whole: its content at full height.
                    @MainActor func scrollViews(_ view: NSView) -> [NSScrollView] {
                        if let s = view as? NSScrollView { return [s] }
                        return view.subviews.flatMap { scrollViews($0) }
                    }
                    if let doc = scrollViews(v).compactMap(\.documentView).max(by: { $0.bounds.height < $1.bounds.height }),
                       let full = doc.bitmapImageRepForCachingDisplay(in: doc.bounds) {
                        doc.cacheDisplay(in: doc.bounds, to: full)
                        save(full, "window-\(n)-full.png")
                    }
                }
                for (name, img) in [("idle", MenuBarIcon.idle), ("processing", MenuBarIcon.processing(0.4)), ("recording", MenuBarIcon.recording)] {
                    let big = NSImage(size: NSSize(width: 72, height: 72), flipped: false) { r in img.draw(in: r); return true }
                    save(big.tiffRepresentation.flatMap(NSBitmapImageRep.init(data:)), "menubar-\(name).png")
                }
                NSApp.terminate(nil)
            }
        }
    }

    /// hesper://record, stop, pause, mark… (Shortcuts, Raycast, a Stream Deck).
    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls { model.handleLink(url) }
    }

    /// The Dock icon (shown while the detached window is open) brings that window back.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if Windows.isDetached { Windows.showMain(model) }
        return false
    }

    // Show our banners even while the app is active.
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                            withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }

    // "Record" / "Stop & create notes" buttons.
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                            withCompletionHandler completionHandler: @escaping () -> Void) {
        let action = response.actionIdentifier
        let meeting = response.notification.request.content.userInfo["meeting"] as? String
        let isUpdate = response.notification.request.content.userInfo["action"] as? String == "update"
        Task { @MainActor in
            if isUpdate { Windows.showSettings(self.model, tab: .about) }
            else { self.model.handleNotificationAction(action, meeting: meeting) }
            completionHandler()
        }
    }
}

/// The menu bar icon: waveform when idle, ● + timer while recording, hourglass while processing.
struct MenuBarLabel: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        if model.isRecording {
            // Not a template image: the red dot means the mic is on, it must stay red.
            Text("\(Image(nsImage: MenuBarIcon.recording)) \(model.isPaused ? "⏸ " : "")\(formatDuration(model.elapsed))")
                .monospacedDigit()
        } else if model.currentJob != nil {
            if let p = model.jobProgress {
                Text("\(Image(nsImage: MenuBarIcon.processing(p))) \(Int(p * 100))%").monospacedDigit()
            } else {
                Image(nsImage: MenuBarIcon.processing(nil))
            }
        } else if model.phase == .settingUp {
            Image(nsImage: MenuBarIcon.processing(model.setupProgress))
        } else {
            Image(nsImage: MenuBarIcon.idle)
        }
    }
}
