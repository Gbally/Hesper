// The silent demo video for the website and README, as if someone used the app through a day of
// meetings: a call is noticed, a pointer clicks, notes are typed, the window is detached and pinned,
// the meeting scrolls, voices get names, the e-mail writes itself, to-dos are ticked off. Every
// screen is the app's own (demo mode screenshots, see `--demo` / `--snapshot` in HesperApp.swift);
// this script only animates them. 1920×1080, 30 fps, H.264, no video tools needed.
//
//   swift scripts/make_video.swift CAPTURES_DIR OUT.mp4 [POSTER.png] [KBPS]
//
// KBPS: 2300 by default (the website). OUT_WIDTH scales the output (16:9): GitHub takes at most 10 MB
// for a video in a README, so that copy is made with `"" 1150 1600`.
//
// CAPTURES_DIR: the menu bar window's screens (home, home-<lang>, rec0, rec1t, rec1s, recn,
// ask-answer, todos, todos-done, week: 760×1160), the detached window (det, detpin), the meeting
// window (meeting, speakers) with its whole scrolling page (meeting-doc, where-doc), Settings → AI
// models (models) and followup.txt. Positions below are in capture pixels.

import AppKit
import AVFoundation

let args = CommandLine.arguments
let caps = URL(fileURLWithPath: args[1])
let out = URL(fileURLWithPath: args[2])
let posterOut = args.count > 3 && !args[3].isEmpty ? URL(fileURLWithPath: args[3]) : nil
let bitrate = args.count > 4 ? Int(args[4])! * 1000 : 2_300_000
let outW = args.count > 5 ? Int(args[5])! : 1920, outH = outW * 9 / 16
let W = 1920, H = 1080, FPS = 30

// MARK: - Palette, type, images

func rgb(_ hex: UInt32, _ a: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat(hex >> 16 & 255) / 255, green: CGFloat(hex >> 8 & 255) / 255, blue: CGFloat(hex & 255) / 255, alpha: a)
}
let duskInk = rgb(0x1B1F2E), horizon = rgb(0x2E3550), star = rgb(0xF2B544)
let windowBg = rgb(0x2A2A2C), titleBar = rgb(0x363638), menuBlue = rgb(0x2F6FE4)
let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!

func rounded(_ size: CGFloat, _ weight: NSFont.Weight) -> NSFont {
    let base = NSFont.systemFont(ofSize: size, weight: weight)
    return NSFont(descriptor: base.fontDescriptor.withDesign(.rounded) ?? base.fontDescriptor, size: size) ?? base
}

/// A capture drawn over the window colour: opaque, so screens can cross-fade and be sampled.
func load(_ name: String) -> CGImage {
    let url = caps.appendingPathComponent(name)
    guard let img = NSImage(contentsOf: url)?.cgImage(forProposedRect: nil, context: nil, hints: nil) else { fatalError("missing \(url.path)") }
    let ctx = CGContext(data: nil, width: img.width, height: img.height, bitsPerComponent: 8, bytesPerRow: img.width * 4, space: sRGB,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.setFillColor(windowBg); ctx.fill(CGRect(x: 0, y: 0, width: img.width, height: img.height))
    ctx.draw(img, in: CGRect(x: 0, y: 0, width: img.width, height: img.height))
    return ctx.makeImage()!
}

func pixel(_ img: CGImage, _ x: Int, _ y: Int) -> CGColor {  // top-left coordinates
    let ctx = CGContext(data: nil, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4, space: sRGB,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.draw(img, in: CGRect(x: -x, y: y - img.height + 1, width: img.width, height: img.height))
    let p = ctx.data!.assumingMemoryBound(to: UInt8.self)
    return CGColor(srgbRed: CGFloat(p[0]) / 255, green: CGFloat(p[1]) / 255, blue: CGFloat(p[2]) / 255, alpha: 1)
}

enum Img {
    static let home = load("home.png"), rec0 = load("rec0.png"), rec1t = load("rec1t.png"), rec1s = load("rec1s.png")
    static let recn = load("recn.png"), answer = load("ask-answer.png"), todos = load("todos.png"), todosDone = load("todos-done.png")
    static let week = load("week.png"), det = load("det.png"), detpin = load("detpin.png")
    static let meeting = load("meeting.png"), speakers = load("speakers.png"), meetingDoc = load("meeting-doc.png")
    static let whereDoc = load("where-doc.png"), models = load("models.png")
    static let languages = ["fr", "es", "pt", "ja", "zh-Hans"].map { load("home-\($0).png") }
}
let followUp = (try? String(contentsOf: caps.appendingPathComponent("followup.txt"))) ?? ""

// Text typed by the "user", in the app's own font size (measured on the real screens).
func fitted(_ s: String, width: CGFloat) -> NSFont {
    let w = (s as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: 26)]).width
    return NSFont.systemFont(ofSize: 26 * width / w)
}
let notesFont = fitted("Budget cap 80k is the real constraint", width: 417)
let fieldFont = fitted("What does Northwind need before going live?", width: 564)
let searchFill = pixel(Img.home, 560, 1022)
let nameFill = pixel(Img.speakers, 900, 216)

// MARK: - Positions (capture pixels) and layout (screen pixels)

enum P {  // the menu bar window (760×1160)
    static let record = CGPoint(x: 88, y: 190), mark = CGPoint(x: 548, y: 333), notes = CGPoint(x: 390, y: 450)
    static let search = CGPoint(x: 260, y: 1022), back = CGPoint(x: 35, y: 38), detach = CGPoint(x: 605, y: 50)
    static let more = CGPoint(x: 711, y: 50), todosTab = CGPoint(x: 636, y: 1120), vendor = CGPoint(x: 42, y: 260)
}
enum D {  // the detached window (906×1264)
    static let pin = CGPoint(x: 754, y: 50), stop = CGPoint(x: 453, y: 619)
}
enum M {  // the meeting window (1120×1360)
    static let docTop: CGFloat = 74, footerTop: CGFloat = 1293
    static let quote = CGPoint(x: 1075, y: 500), listen = CGPoint(x: 258, y: 780), emailArrow = CGPoint(x: 945, y: 1324)
    static let checkVoices = CGPoint(x: 338, y: 285), more = CGPoint(x: 1070, y: 36)
    static let whereBlock = CGRect(x: 80, y: 498 + 74, width: 1005, height: 238)
    static let nameField = CGPoint(x: 420, y: 216), speakersBack = CGPoint(x: 34, y: 36)
    static let saveNames = CGPoint(x: 934, y: 1321), remembers = CGRect(x: 28, y: 100, width: 1070, height: 56)
}
enum S {  // Settings → AI models (1560×1080)
    static let thisMac = CGRect(x: 507, y: 40, width: 1011, height: 75), recommended = CGRect(x: 507, y: 425, width: 1011, height: 70)
}

struct Placement {
    var origin: CGPoint, scale: CGFloat
    func at(_ p: CGPoint) -> CGPoint { CGPoint(x: origin.x + p.x * scale, y: origin.y + p.y * scale) }
    func rect(_ r: CGRect) -> CGRect { CGRect(x: origin.x + r.minX * scale, y: origin.y + r.minY * scale, width: r.width * scale, height: r.height * scale) }
}
let bar: CGFloat = 56  // a window's title bar, in capture pixels
let pop = Placement(origin: CGPoint(x: 1260, y: 62), scale: 0.78)          // under the menu bar icon
let popSize = CGSize(width: 760 * pop.scale, height: 1160 * pop.scale)
let menuIcon = CGPoint(x: pop.origin.x + popSize.width / 2, y: 22)
let mScale: CGFloat = 0.664
let mFrame = CGRect(x: 1010, y: 70, width: 1120 * mScale, height: (1360 + bar) * mScale)
let mContent = Placement(origin: CGPoint(x: mFrame.minX, y: mFrame.minY + bar * mScale), scale: mScale)
let dScale: CGFloat = 0.66
let dFrame = CGRect(x: 1250, y: 110, width: 906 * dScale, height: (1264 + bar) * dScale)
let dContent = Placement(origin: CGPoint(x: dFrame.minX, y: dFrame.minY + bar * dScale), scale: dScale)
let sScale: CGFloat = 0.6
let sFrame = CGRect(x: 950, y: 210, width: 1560 * sScale, height: (1080 + bar) * sScale)
let sContent = Placement(origin: CGPoint(x: sFrame.minX, y: sFrame.minY + bar * sScale), scale: sScale)
let banner = CGRect(x: 1250, y: 60, width: 646, height: 124)
let bannerRecord = CGPoint(x: banner.maxX - 78, y: banner.midY + 2)

// MARK: - Timeline

func clamp(_ x: Double) -> Double { min(max(x, 0), 1) }
func ease(_ x: Double) -> CGFloat { CGFloat(1 - pow(1 - clamp(x), 3)) }                       // ease-out
func easeInOut(_ x: Double) -> CGFloat { let t = clamp(x); return CGFloat(t < 0.5 ? 4 * t * t * t : 1 - pow(-2 * t + 2, 3) / 2) }
func between(_ t: Double, _ a: Double, _ b: Double) -> Bool { t >= a && t < b }
/// 0 → 1 over [a, a+fade], 1 → 0 over [b-fade, b].
func window(_ t: Double, _ a: Double, _ b: Double, fade: Double = 0.35) -> CGFloat {
    t < a || t > b ? 0 : min(ease((t - a) / fade), CGFloat(clamp((b - t) / fade)))
}

struct Caption { var a: Double, b: Double, text: String, sub: String }
let captions = [
    Caption(a: 2.5, b: 6.4, text: "Records *any call*", sub: "It notices when Teams, Zoom or Meet uses\nthe microphone. One click, and it records."),
    Caption(a: 6.4, b: 10.4, text: "Pause, *mark* moments,\ntype your notes", sub: "⌃⌥M marks what matters, from any app.\nA line with [ ] becomes your to-do."),
    Caption(a: 10.4, b: 14.0, text: "Keep it *open*\nduring the call", sub: "Detach it into its own window,\nand pin it on top of your call."),
    Caption(a: 14.1, b: 17.1, text: "Notes in *minutes*", sub: "Summary, decisions, and to-dos\nwith owners and real due dates."),
    Caption(a: 17.1, b: 21.1, text: "See *where* it was said", sub: "Every to-do shows the lines it came from —\nand you can listen to the moment."),
    Caption(a: 21.1, b: 25.2, text: "Knows *who* spoke", sub: "Name a voice once: Hesper remembers it,\nand names that person in every next meeting."),
    Caption(a: 25.2, b: 28.5, text: "Marked moments,\n*talk time*", sub: "What was said before each mark,\nand who spoke how long."),
    Caption(a: 28.5, b: 33.1, text: "A follow-up e-mail,\n*written for you*", sub: "From the notes, in the meeting's language,\nready to send to the attendees."),
    Caption(a: 33.1, b: 36.6, text: "Your to-dos,\n*where you work*", sub: "Reminders, Apple Notes, Things,\nor your Obsidian folder."),
    Caption(a: 36.8, b: 42.2, text: "*Ask* all your meetings", sub: "Answers from your own notes,\nwith their sources."),
    Caption(a: 42.2, b: 46.4, text: "All your *to-dos*,\nwith due dates", sub: "From every meeting, yours first.\nTick them off as you go."),
    Caption(a: 46.4, b: 50.7, text: "Your *week*, every Friday", sub: "Meetings, open and overdue to-dos,\nthe decisions."),
    Caption(a: 50.9, b: 55.0, text: "Runs on *your*\ncomputer", sub: "It picks the AI models that fit your Mac or PC.\nNo account, no cloud, nothing uploaded."),
    Caption(a: 55.0, b: 58.7, text: "In *6 languages*", sub: "English, Français, Español, Português, 日本語, 中文 —\nand notes for meetings in 99 languages."),
]

/// The pointer: where it goes and when, and when it clicks.
struct Move { var a: Double, b: Double, to: CGPoint }
let moves: [Move] = [
    Move(a: 2.95, b: 3.9, to: bannerRecord),
    Move(a: 4.6, b: 5.2, to: menuIcon),
    Move(a: 5.8, b: 6.3, to: pop.at(P.mark)),
    Move(a: 6.8, b: 7.2, to: pop.at(P.notes)),
    Move(a: 10.0, b: 10.5, to: pop.at(P.detach)),
    Move(a: 11.1, b: 11.7, to: dContent.at(D.pin)),
    Move(a: 12.7, b: 13.3, to: dContent.at(D.stop)),
    Move(a: 14.2, b: 15.0, to: mContent.at(CGPoint(x: 600, y: 560))),
    Move(a: 15.0, b: 16.4, to: mContent.at(CGPoint(x: 760, y: 680))),
    Move(a: 16.9, b: 17.5, to: mContent.at(M.quote)),
    Move(a: 18.1, b: 18.7, to: mContent.at(M.listen)),
    Move(a: 20.6, b: 21.2, to: mContent.at(M.checkVoices)),
    Move(a: 21.7, b: 22.2, to: mContent.at(M.nameField)),
    Move(a: 23.1, b: 23.5, to: mContent.at(M.saveNames)),
    Move(a: 25.0, b: 25.4, to: mContent.at(CGPoint(x: 640, y: 700))),
    Move(a: 28.1, b: 28.7, to: mContent.at(M.emailArrow)),
    Move(a: 29.2, b: 29.6, to: mContent.at(CGPoint(x: 720, y: 1214))),
    Move(a: 30.5, b: 31.1, to: CGPoint(x: 1730, y: 900)),
    Move(a: 33.0, b: 33.6, to: mContent.at(M.more)),
    Move(a: 34.0, b: 34.4, to: mContent.at(CGPoint(x: 800, y: 376))),
    Move(a: 36.7, b: 37.3, to: menuIcon),
    Move(a: 37.8, b: 38.3, to: pop.at(P.search)),
    Move(a: 40.3, b: 40.8, to: pop.at(CGPoint(x: 600, y: 700))),
    Move(a: 41.9, b: 42.3, to: pop.at(P.back)),
    Move(a: 42.7, b: 43.2, to: pop.at(P.todosTab)),
    Move(a: 43.7, b: 44.2, to: pop.at(P.vendor)),
    Move(a: 46.2, b: 46.6, to: pop.at(P.back)),
    Move(a: 47.0, b: 47.4, to: pop.at(P.more)),
    Move(a: 47.8, b: 48.1, to: pop.at(CGPoint(x: 430, y: 72 + 10 + 44 * 3 + 22))),
    Move(a: 48.8, b: 49.4, to: pop.at(CGPoint(x: 560, y: 820))),
    Move(a: 51.0, b: 51.9, to: sContent.at(CGPoint(x: 1300, y: 300))),
    Move(a: 52.9, b: 53.5, to: sContent.at(CGPoint(x: 1240, y: 470))),
]
let clicks: [Double] = [4.15, 5.3, 6.4, 7.3, 10.6, 11.95, 13.6, 17.6, 18.8, 21.3, 22.3, 23.6, 28.8, 29.8, 33.7, 34.6, 37.4, 38.4,
                        42.4, 43.3, 44.35, 46.7, 47.5, 48.25]
let pointerShown = (2.7, 55.0)

func pointer(at t: Double) -> CGPoint {
    var p = CGPoint(x: 1000, y: 650)
    for m in moves {
        if t >= m.b { p = m.to; continue }
        if t > m.a {
            let k = easeInOut((t - m.a) / (m.b - m.a))
            return CGPoint(x: p.x + (m.to.x - p.x) * k, y: p.y + (m.to.y - p.y) * k)
        }
        break
    }
    return p
}

/// What the menu bar window shows at time t (it cross-fades from the previous screen).
let popStates: [(Double, CGImage)] = [
    (0, Img.home), (5.35, Img.rec0), (6.45, Img.rec1t), (7.35, Img.rec1s), (9.9, Img.recn),
    (37.0, Img.home), (40.1, Img.answer), (42.45, Img.home), (43.35, Img.todos), (44.9, Img.todosDone), (46.75, Img.home), (48.3, Img.week),
    (55.0, Img.home), (55.75, Img.languages[0]), (56.3, Img.languages[1]), (56.85, Img.languages[2]), (57.4, Img.languages[3]),
    (57.95, Img.languages[4]),
]
let popShown = [(5.35, 10.9), (37.45, 50.8), (55.1, 58.6)]
let notesTyped = (start: 7.45, cps: 32.0, text: "Budget cap 80k is the real constraint\n[ ] ask Finance for a phased invoice")
let askTyped = (start: 38.5, cps: 30.0, text: "What does Northwind need before going live?")
let nameTyped = (start: 22.45, cps: 22.0, text: "Lisa Moreau")
let total = 62.6

// MARK: - Drawing (top-left coordinates)

final class Canvas {
    let ctx: CGContext
    init(_ ctx: CGContext) { self.ctx = ctx }
    func flip(_ r: CGRect) -> CGRect { CGRect(x: r.minX, y: CGFloat(H) - r.maxY, width: r.width, height: r.height) }
    func flip(_ p: CGPoint) -> CGPoint { CGPoint(x: p.x, y: CGFloat(H) - p.y) }

    func background() {
        ctx.setFillColor(duskInk); ctx.fill(CGRect(x: 0, y: 0, width: W, height: H))
        let g = CGGradient(colorsSpace: sRGB, colors: [rgb(0x262B42), duskInk] as CFArray, locations: [0, 1])!
        ctx.drawLinearGradient(g, start: CGPoint(x: 0, y: CGFloat(H)), end: CGPoint(x: CGFloat(W), y: 0),
                               options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        let glow = CGGradient(colorsSpace: sRGB, colors: [rgb(0xF2B544, 0.07), rgb(0xF2B544, 0)] as CFArray, locations: [0, 1])!
        ctx.drawRadialGradient(glow, startCenter: CGPoint(x: 1620, y: 900), startRadius: 0, endCenter: CGPoint(x: 1620, y: 900),
                               endRadius: 700, options: [])
    }

    @discardableResult
    func text(_ s: String, at p: CGPoint, width: CGFloat, font: NSFont, color: CGColor, accent: CGColor? = nil, alpha: CGFloat = 1,
              lineSpacing: CGFloat = 4, center: Bool = false) -> CGFloat {
        let para = NSMutableParagraphStyle()
        para.lineSpacing = lineSpacing
        para.alignment = center ? .center : .left
        let attr = NSMutableAttributedString()
        var on = false
        for (i, part) in s.components(separatedBy: "*").enumerated() {
            if i > 0 { on.toggle() }
            let base = on ? accent ?? color : color
            let c = base.copy(alpha: base.alpha * alpha)!
            attr.append(NSAttributedString(string: part, attributes: [.font: font, .foregroundColor: NSColor(cgColor: c)!, .paragraphStyle: para]))
        }
        let size = attr.boundingRect(with: CGSize(width: width, height: 4000), options: [.usesLineFragmentOrigin]).size
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
        attr.draw(with: flip(CGRect(x: p.x, y: p.y, width: width, height: ceil(size.height))), options: [.usesLineFragmentOrigin])
        NSGraphicsContext.restoreGraphicsState()
        return ceil(size.height)
    }

    func caption(_ c: Caption, t: Double) {
        let a = window(t, c.a, c.b, fade: 0.4)
        guard a > 0 else { return }
        let lift = (1 - ease((t - c.a) / 0.4)) * 24
        let h = text(c.text, at: CGPoint(x: 130, y: 390 + lift), width: 760, font: rounded(70, .heavy), color: rgb(0xFFFFFF),
                     accent: star, alpha: a, lineSpacing: 2)
        text(c.sub, at: CGPoint(x: 132, y: 390 + h + 28 + lift), width: 780, font: rounded(32, .medium), color: rgb(0xB8BCCB),
             alpha: a, lineSpacing: 8)
    }

    func shadowed(_ path: CGPath, fill: CGColor, blur: CGFloat = 70) {
        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: -24), blur: blur, color: rgb(0x000000, 0.55))
        ctx.addPath(path); ctx.setFillColor(fill); ctx.fillPath()
        ctx.restoreGState()
    }

    func image(_ i: CGImage, in r: CGRect, alpha: CGFloat = 1) {
        ctx.saveGState(); ctx.setAlpha(alpha); ctx.interpolationQuality = .high
        ctx.draw(i, in: flip(r))
        ctx.restoreGState()
    }

    /// A slice of a capture (top-left source rectangle) into a screen rectangle.
    func slice(_ i: CGImage, from src: CGRect, to dst: CGRect, alpha: CGFloat = 1) {
        guard let part = i.cropping(to: src.integral) else { return }
        image(part, in: dst, alpha: alpha)
    }

    func ring(_ r: CGRect, alpha: CGFloat) {  // an amber ring around what matters
        guard alpha > 0 else { return }
        ctx.saveGState()
        ctx.setShadow(offset: .zero, blur: 22, color: star.copy(alpha: 0.8 * alpha)!)
        ctx.addPath(CGPath(roundedRect: flip(r.insetBy(dx: -6, dy: -6)), cornerWidth: 14, cornerHeight: 14, transform: nil))
        ctx.setStrokeColor(star.copy(alpha: alpha)!); ctx.setLineWidth(4); ctx.strokePath()
        ctx.restoreGState()
    }

    // The menu bar: a thin translucent strip with Hesper's icon (red dot while recording) and the clock.
    func menuBar(alpha: CGFloat, recording: Bool, pressed: Bool) {
        guard alpha > 0 else { return }
        ctx.saveGState(); ctx.setAlpha(alpha)
        ctx.setFillColor(rgb(0x000000, 0.32)); ctx.fill(flip(CGRect(x: 0, y: 0, width: CGFloat(W), height: 44)))
        ctx.setFillColor(rgb(0xFFFFFF, 0.08)); ctx.fill(flip(CGRect(x: 0, y: 44, width: CGFloat(W), height: 1)))
        if pressed {
            ctx.addPath(CGPath(roundedRect: flip(CGRect(x: menuIcon.x - 26, y: 5, width: 52, height: 34)), cornerWidth: 8, cornerHeight: 8, transform: nil))
            ctx.setFillColor(rgb(0xFFFFFF, 0.2)); ctx.fillPath()
        }
        glyph(center: menuIcon, size: 30, color: rgb(0xFFFFFF, 0.95))
        if recording {
            ctx.setFillColor(rgb(0xD93A3A)); ctx.fillEllipse(in: flip(CGRect(x: menuIcon.x + 6, y: 8, width: 10, height: 10)))
        }
        text("Tue 24 Nov  10:02", at: CGPoint(x: menuIcon.x + 60, y: 9), width: 260, font: NSFont.systemFont(ofSize: 22, weight: .medium),
             color: rgb(0xFFFFFF, 0.95))
        ctx.restoreGState()
    }

    /// Hesper's menu bar glyph: the star over its horizon line.
    func glyph(center c: CGPoint, size: CGFloat, color: CGColor) {
        let u = size / 18, o = CGPoint(x: c.x - 9 * u, y: c.y - 9 * u)
        func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { flip(CGPoint(x: o.x + x * u, y: o.y + y * u)) }
        let top = p(9, 2), right = p(14, 7), bottom = p(9, 12), left = p(4, 7), mid = p(9, 7)
        let path = CGMutablePath(); path.move(to: top)
        for (a, b) in [(top, right), (right, bottom), (bottom, left), (left, top)] {
            path.addCurve(to: b, control1: CGPoint(x: a.x + 2 / 3 * (mid.x - a.x), y: a.y + 2 / 3 * (mid.y - a.y)),
                          control2: CGPoint(x: b.x + 2 / 3 * (mid.x - b.x), y: b.y + 2 / 3 * (mid.y - b.y)))
        }
        ctx.addPath(path); ctx.setFillColor(color); ctx.fillPath()
        ctx.addPath(CGPath(roundedRect: flip(CGRect(x: o.x + 2 * u, y: o.y + 14.2 * u, width: 14 * u, height: 1.6 * u)), cornerWidth: 0.8 * u,
                           cornerHeight: 0.8 * u, transform: nil))
        ctx.fillPath()
    }

    /// A macOS notification banner: "Record this call?" with its Record button.
    func notification(alpha: CGFloat, slide: CGFloat, hover: Bool) {
        guard alpha > 0 else { return }
        let r = banner.offsetBy(dx: slide, dy: 0)
        ctx.saveGState(); ctx.setAlpha(alpha); ctx.beginTransparencyLayer(auxiliaryInfo: nil)
        let shape = CGPath(roundedRect: flip(r), cornerWidth: 26, cornerHeight: 26, transform: nil)
        shadowed(shape, fill: rgb(0x2E2F36, 0.97), blur: 40)
        ctx.addPath(shape); ctx.setStrokeColor(rgb(0xFFFFFF, 0.12)); ctx.setLineWidth(1.5); ctx.strokePath()
        icon(center: CGPoint(x: r.minX + 52, y: r.midY), size: 64, alpha: 1)
        text("Record “Q4 renewal with Northwind”?", at: CGPoint(x: r.minX + 100, y: r.minY + 22), width: 420,
             font: NSFont.systemFont(ofSize: 22, weight: .semibold), color: rgb(0xFFFFFF))
        text("Microsoft Teams is using the microphone. Click Record to take notes with Hesper.", at: CGPoint(x: r.minX + 100, y: r.minY + 52),
             width: 400, font: NSFont.systemFont(ofSize: 20), color: rgb(0xC9CAD2), lineSpacing: 2)
        let b = CGRect(x: r.maxX - 140, y: r.midY - 22, width: 124, height: 48)
        ctx.addPath(CGPath(roundedRect: flip(b), cornerWidth: 12, cornerHeight: 12, transform: nil))
        ctx.setFillColor(hover ? rgb(0x5A5C66) : rgb(0x464852)); ctx.fillPath()
        text("Record", at: CGPoint(x: b.minX, y: b.minY + 11), width: b.width, font: NSFont.systemFont(ofSize: 21, weight: .medium),
             color: rgb(0xFFFFFF), center: true)
        ctx.endTransparencyLayer(); ctx.restoreGState()
    }

    /// The menu bar window: no title bar, rounded, the current screen cross-faded from the last one.
    func popover(t: Double, alpha: CGFloat, drop: CGFloat, overlay: (Canvas) -> Void) {
        guard alpha > 0 else { return }
        let frame = CGRect(x: pop.origin.x, y: pop.origin.y - drop, width: popSize.width, height: popSize.height)
        let shape = CGPath(roundedRect: flip(frame), cornerWidth: 22, cornerHeight: 22, transform: nil)
        ctx.saveGState(); ctx.setAlpha(alpha); ctx.beginTransparencyLayer(auxiliaryInfo: nil)
        shadowed(shape, fill: windowBg)
        ctx.saveGState(); ctx.addPath(shape); ctx.clip()
        let i = popStates.lastIndex { $0.0 <= t } ?? 0
        let k = i > 0 ? ease((t - popStates[i].0) / 0.18) : 1
        if k < 1 { image(popStates[i - 1].1, in: frame) }
        image(popStates[i].1, in: frame, alpha: k)
        ctx.translateBy(x: 0, y: drop)  // overlays use the window's own coordinates
        overlay(self)
        ctx.restoreGState()
        ctx.addPath(shape); ctx.setStrokeColor(rgb(0xFFFFFF, 0.14)); ctx.setLineWidth(2); ctx.strokePath()
        ctx.endTransparencyLayer(); ctx.restoreGState()
    }

    /// A titled window holding `draw`'s content (capture coordinates through `content`).
    func appWindow(frame f: CGRect, scale s: CGFloat, title: String, alpha: CGFloat, lift: CGFloat, draw: (Canvas, Placement) -> Void) {
        guard alpha > 0 else { return }
        let f = f.offsetBy(dx: 0, dy: lift)
        let content = Placement(origin: CGPoint(x: f.minX, y: f.minY + bar * s), scale: s)
        let shape = CGPath(roundedRect: flip(f), cornerWidth: 22 * s, cornerHeight: 22 * s, transform: nil)
        ctx.saveGState(); ctx.setAlpha(alpha); ctx.beginTransparencyLayer(auxiliaryInfo: nil)
        shadowed(shape, fill: windowBg)
        ctx.saveGState(); ctx.addPath(shape); ctx.clip()
        ctx.setFillColor(titleBar); ctx.fill(flip(CGRect(x: f.minX, y: f.minY, width: f.width, height: bar * s)))
        ctx.setFillColor(rgb(0x000000, 0.5)); ctx.fill(flip(CGRect(x: f.minX, y: f.minY + bar * s - 1, width: f.width, height: 1.5)))
        draw(self, content)
        ctx.restoreGState()
        for (i, c) in [0xFF5F57, 0xFEBC2E, 0x28C840].enumerated() {
            ctx.setFillColor(rgb(UInt32(c)))
            ctx.fillEllipse(in: flip(CGRect(x: f.minX + (26 + CGFloat(i) * 40) * s, y: f.minY + (bar / 2 - 12) * s, width: 24 * s, height: 24 * s)))
        }
        text(title, at: CGPoint(x: f.minX, y: f.minY + (bar / 2 - 15) * s), width: f.width, font: rounded(25 * s, .semibold),
             color: rgb(0xDDDDDD), center: true)
        ctx.addPath(shape); ctx.setStrokeColor(rgb(0xFFFFFF, 0.12)); ctx.setLineWidth(2); ctx.strokePath()
        ctx.endTransparencyLayer(); ctx.restoreGState()
    }

    /// The meeting window's content: the page scrolled to `scroll` between its fixed header and footer.
    func meetingPage(_ c: Placement, scroll: CGFloat, whereOpen: CGFloat) {
        let visible = M.footerTop - M.docTop
        let pageRect = c.rect(CGRect(x: 0, y: M.docTop, width: 1120, height: visible))
        for (doc, a) in [(Img.meetingDoc, 1 - whereOpen), (Img.whereDoc, whereOpen)] where a > 0 {
            let y = min(scroll, CGFloat(doc.height) - visible)
            slice(doc, from: CGRect(x: 0, y: y, width: 1120, height: visible), to: pageRect, alpha: a)
        }
        slice(Img.meeting, from: CGRect(x: 0, y: 0, width: 1120, height: M.docTop), to: c.rect(CGRect(x: 0, y: 0, width: 1120, height: M.docTop)))
        slice(Img.meeting, from: CGRect(x: 0, y: M.footerTop, width: 1120, height: 1360 - M.footerTop),
              to: c.rect(CGRect(x: 0, y: M.footerTop, width: 1120, height: 1360 - M.footerTop)))
    }

    /// A native-looking menu; the item under the pointer is highlighted. `anchor`: its top-right corner.
    func menu(_ items: [String], topRight anchor: CGPoint, width: CGFloat, scale s: CGFloat, alpha: CGFloat, pointer: CGPoint) {
        guard alpha > 0 else { return }
        let rowH = 44 * s, pad = 10 * s
        let h = items.reduce(0) { $0 + ($1 == "-" ? 18 * s : rowH) } + pad * 2
        let r = CGRect(x: anchor.x - width, y: anchor.y, width: width, height: h)
        ctx.saveGState(); ctx.setAlpha(alpha); ctx.beginTransparencyLayer(auxiliaryInfo: nil)
        let shape = CGPath(roundedRect: flip(r), cornerWidth: 12 * s, cornerHeight: 12 * s, transform: nil)
        shadowed(shape, fill: rgb(0x2C2C2F, 0.98), blur: 40)
        ctx.addPath(shape); ctx.setStrokeColor(rgb(0xFFFFFF, 0.14)); ctx.setLineWidth(1.5); ctx.strokePath()
        var y = r.minY + pad
        for item in items {
            if item == "-" {
                ctx.setFillColor(rgb(0xFFFFFF, 0.12)); ctx.fill(flip(CGRect(x: r.minX + 20 * s, y: y + 9 * s, width: width - 40 * s, height: 1.5)))
                y += 18 * s; continue
            }
            let row = CGRect(x: r.minX + 10 * s, y: y, width: width - 20 * s, height: rowH)
            let on = row.contains(pointer)
            if on {
                ctx.addPath(CGPath(roundedRect: flip(row), cornerWidth: 8 * s, cornerHeight: 8 * s, transform: nil))
                ctx.setFillColor(menuBlue); ctx.fillPath()
            }
            text(item, at: CGPoint(x: row.minX + 22 * s, y: y + 8 * s), width: width, font: NSFont.systemFont(ofSize: 26 * s),
                 color: rgb(0xFFFFFF, on ? 1 : 0.92))
            y += rowH
        }
        ctx.endTransparencyLayer(); ctx.restoreGState()
    }

    /// Typed text with a blinking caret, at a capture position (left, line centre).
    func typed(_ s: String, place: Placement, left: CGFloat, centerY: CGFloat, font: NSFont, lineHeight: CGFloat, t: Double) {
        let lines = s.components(separatedBy: "\n")
        let screenFont = NSFont.systemFont(ofSize: font.pointSize * place.scale)
        for (i, line) in lines.enumerated() {
            let top = centerY + CGFloat(i) * lineHeight - (font.ascender - font.descender) / 2
            text(line, at: place.at(CGPoint(x: left, y: top)), width: 2000, font: screenFont, color: rgb(0xECECEC), lineSpacing: 0)
        }
        if Int(t * 2) % 2 == 0 {  // the caret
            let last = lines.last ?? ""
            let w = (last as NSString).size(withAttributes: [.font: font]).width
            let top = place.at(CGPoint(x: left + w + 2, y: centerY + CGFloat(lines.count - 1) * lineHeight - font.pointSize * 0.55))
            ctx.setFillColor(rgb(0xF2B544)); ctx.fill(flip(CGRect(x: top.x, y: top.y, width: 2.5, height: font.pointSize * 1.1 * place.scale)))
        }
    }

    /// A field's placeholder hidden behind its own colour (before typing into it).
    func cover(_ r: CGRect, place: Placement, color: CGColor) {
        ctx.setFillColor(color); ctx.fill(flip(place.rect(r)))
    }

    /// A to-do's circle being ticked: green, with a check mark.
    func tick(_ p: CGPoint, place: Placement, k: CGFloat) {
        guard k > 0 else { return }
        let c = place.at(p), r = 13 * place.scale * (0.6 + 0.4 * k)
        ctx.saveGState(); ctx.setAlpha(min(1, k * 1.5))
        ctx.setFillColor(rgb(0x30D158)); ctx.fillEllipse(in: flip(CGRect(x: c.x - r, y: c.y - r, width: 2 * r, height: 2 * r)))
        let path = CGMutablePath()
        path.addLines(between: [CGPoint(x: c.x - r * 0.45, y: c.y), CGPoint(x: c.x - r * 0.1, y: c.y + r * 0.38),
                                CGPoint(x: c.x + r * 0.5, y: c.y - r * 0.35)].map { flip($0) })
        ctx.addPath(path); ctx.setStrokeColor(rgb(0xFFFFFF)); ctx.setLineWidth(3); ctx.setLineCap(.round); ctx.setLineJoin(.round); ctx.strokePath()
        ctx.restoreGState()
    }

    /// A note over the screen (not the app's own UI): a tick, a title and a line.
    func callout(_ title: String, _ line: String, frame r: CGRect, alpha: CGFloat) {
        guard alpha > 0 else { return }
        let r = r.offsetBy(dx: 0, dy: (1 - alpha) * 12)
        ctx.saveGState(); ctx.setAlpha(alpha); ctx.beginTransparencyLayer(auxiliaryInfo: nil)
        let shape = CGPath(roundedRect: flip(r), cornerWidth: 18, cornerHeight: 18, transform: nil)
        shadowed(shape, fill: rgb(0x1F2336, 0.98), blur: 30)
        ctx.addPath(shape); ctx.setStrokeColor(star); ctx.setLineWidth(3); ctx.strokePath()
        tick(.zero, place: Placement(origin: CGPoint(x: r.minX + 46, y: r.midY), scale: 1.7), k: 1)
        text(title, at: CGPoint(x: r.minX + 88, y: r.minY + 18), width: r.width - 100, font: rounded(25, .bold), color: rgb(0xFFFFFF))
        text(line, at: CGPoint(x: r.minX + 88, y: r.minY + 54), width: r.width - 100, font: rounded(21, .medium), color: rgb(0xC9CAD2))
        ctx.endTransparencyLayer(); ctx.restoreGState()
    }

    /// The follow-up e-mail as a draft, writing itself (`shown` = 0…1 of the text).
    func email(_ body: String, frame f: CGRect, alpha: CGFloat, shown: CGFloat) {
        guard alpha > 0 else { return }
        var lines = body.components(separatedBy: "\n")
        var subject = "Follow-up"
        if let first = lines.first, first.lowercased().hasPrefix("subject:") {
            subject = String(first.dropFirst(8)).trimmingCharacters(in: .whitespaces)
            lines.removeFirst()
        }
        let full = lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
        let body = String(full.prefix(Int(CGFloat(full.count) * shown)))
        appWindow(frame: f, scale: 1, title: "New Message", alpha: alpha, lift: 0) { c, _ in
            var y = f.minY + bar + 22
            for (k, v) in [("To:", "Lisa Moreau, David Chen, Emma Walsh"), ("Subject:", subject)] {
                c.text(k, at: CGPoint(x: f.minX + 34, y: y), width: 120, font: rounded(23, .medium), color: rgb(0x9A9AA0))
                c.text(v, at: CGPoint(x: f.minX + 150, y: y), width: f.width - 190, font: NSFont.systemFont(ofSize: 23), color: rgb(0xEDEDED))
                y += 40
                c.ctx.setFillColor(rgb(0xFFFFFF, 0.1)); c.ctx.fill(c.flip(CGRect(x: f.minX + 30, y: y - 6, width: f.width - 60, height: 1.5)))
                y += 8
            }
            c.text(body, at: CGPoint(x: f.minX + 34, y: y + 10), width: f.width - 68, font: NSFont.systemFont(ofSize: 22), color: rgb(0xE6E6E6),
                   lineSpacing: 6)
        }
    }

    func toast(_ s: String, center c: CGPoint, alpha: CGFloat, scale: CGFloat) {
        guard alpha > 0 else { return }
        let font = NSFont.systemFont(ofSize: 24 * scale)
        let w = (s as NSString).size(withAttributes: [.font: font]).width + 40 * scale, h = 44 * scale
        let r = CGRect(x: c.x - w / 2, y: c.y - h / 2, width: w, height: h)
        ctx.saveGState(); ctx.setAlpha(alpha)
        shadowed(CGPath(roundedRect: flip(r), cornerWidth: h / 2, cornerHeight: h / 2, transform: nil), fill: rgb(0x3A3A3E), blur: 16)
        text(s, at: CGPoint(x: r.minX, y: r.minY + 9 * scale), width: w, font: font, color: rgb(0xFFFFFF), center: true)
        ctx.restoreGState()
    }

    /// The macOS pointer; pressed = slightly smaller.
    func pointer(_ p: CGPoint, pressed: Bool, alpha: CGFloat) {
        guard alpha > 0 else { return }
        let s: CGFloat = pressed ? 1.9 : 2.1
        let pts: [(CGFloat, CGFloat)] = [(0, 0), (0, 16.5), (3.9, 12.8), (6.5, 19), (9, 18), (6.4, 11.9), (11.8, 11.9)]
        let path = CGMutablePath()
        path.addLines(between: pts.map { flip(CGPoint(x: p.x + $0.0 * s, y: p.y + $0.1 * s)) })
        path.closeSubpath()
        ctx.saveGState(); ctx.setAlpha(alpha)
        ctx.setShadow(offset: CGSize(width: 0, height: -3), blur: 6, color: rgb(0x000000, 0.5))
        ctx.addPath(path); ctx.setFillColor(rgb(0x000000)); ctx.fillPath()
        ctx.setShadow(offset: .zero, blur: 0, color: nil)
        ctx.addPath(path); ctx.setStrokeColor(rgb(0xFFFFFF)); ctx.setLineWidth(2.4); ctx.setLineJoin(.round); ctx.strokePath()
        ctx.restoreGState()
    }

    func ripple(_ p: CGPoint, age: Double) {
        guard age >= 0, age < 0.45 else { return }
        let k = ease(age / 0.45), r = 10 + 34 * k
        ctx.saveGState()
        ctx.setStrokeColor(star.copy(alpha: 0.85 * (1 - k))!); ctx.setLineWidth(4)
        ctx.strokeEllipse(in: flip(CGRect(x: p.x - r, y: p.y - r, width: 2 * r, height: 2 * r)))
        ctx.setFillColor(star.copy(alpha: 0.25 * (1 - k))!)
        ctx.fillEllipse(in: flip(CGRect(x: p.x - r, y: p.y - r, width: 2 * r, height: 2 * r)))
        ctx.restoreGState()
    }

    func icon(center: CGPoint, size: CGFloat, alpha: CGFloat) {
        let r = CGRect(x: center.x - size / 2, y: center.y - size / 2, width: size, height: size)
        let f = flip(r), u = size / 100
        func map(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: f.minX + x * u, y: f.maxY - y * u) }
        ctx.saveGState(); ctx.setAlpha(alpha)
        let square = CGPath(roundedRect: f, cornerWidth: 22 * u, cornerHeight: 22 * u, transform: nil)
        shadowed(square, fill: duskInk, blur: size > 100 ? 40 : 8)
        ctx.saveGState(); ctx.addPath(square); ctx.clip()
        let c = map(50, 132)
        let planet = CGRect(x: c.x - 64 * u, y: c.y - 64 * u, width: 128 * u, height: 128 * u)
        ctx.setFillColor(horizon); ctx.fillEllipse(in: planet)
        ctx.setStrokeColor(star.copy(alpha: 0.9)!); ctx.setLineWidth(2.2 * u); ctx.strokeEllipse(in: planet)
        ctx.restoreGState()
        let top = map(50, 22), right = map(64, 38), bottom = map(50, 54), left = map(36, 38), mid = map(50, 38)
        let p = CGMutablePath(); p.move(to: top)
        for (a, b) in [(top, right), (right, bottom), (bottom, left), (left, top)] {
            p.addCurve(to: b, control1: CGPoint(x: a.x + 2 / 3 * (mid.x - a.x), y: a.y + 2 / 3 * (mid.y - a.y)),
                       control2: CGPoint(x: b.x + 2 / 3 * (mid.x - b.x), y: b.y + 2 / 3 * (mid.y - b.y)))
        }
        p.closeSubpath()
        ctx.addPath(p); ctx.setFillColor(star); ctx.fillPath()
        ctx.restoreGState()
    }

    func card(t: Double, a: Double, b: Double, end: Bool) {
        let alpha = window(t, a, b, fade: 0.45)
        guard alpha > 0 else { return }
        let lift = (1 - ease((t - a) / 0.45)) * 40, zoom = 1 + 0.025 * CGFloat((t - a) / (b - a))
        icon(center: CGPoint(x: CGFloat(W) / 2, y: 330 + lift), size: 200 * zoom, alpha: alpha)
        text("Hesper", at: CGPoint(x: 0, y: 470 + lift), width: CGFloat(W), font: rounded(118, .heavy), color: rgb(0xFFFFFF), alpha: alpha, center: true)
        if end {
            text("Everything stays *on your computer*.", at: CGPoint(x: 0, y: 640 + lift), width: CGFloat(W), font: rounded(46, .semibold),
                 color: rgb(0xFFFFFF), accent: star, alpha: alpha, center: true)
            text("Free and open source · Mac and Windows", at: CGPoint(x: 0, y: 722 + lift), width: CGFloat(W), font: rounded(34, .medium),
                 color: rgb(0xB8BCCB), alpha: alpha, center: true)
            text("gbally.github.io/Hesper", at: CGPoint(x: 0, y: 800 + lift), width: CGFloat(W), font: rounded(38, .bold), color: star,
                 alpha: alpha, center: true)
        } else {
            text("Private meeting notes, *fully offline*", at: CGPoint(x: 0, y: 640 + lift), width: CGFloat(W), font: rounded(50, .semibold),
                 color: rgb(0xFFFFFF), accent: star, alpha: alpha, center: true)
        }
    }
}

// MARK: - One frame

func frame(_ c: Canvas, t: Double) {
    c.background()
    c.card(t: t, a: -0.45, b: 2.45, end: false)  // already shown on frame 0: players like GitHub's use it as the thumbnail
    c.card(t: t, a: 58.8, b: total, end: true)
    for cap in captions { c.caption(cap, t: t) }
    let ptr = pointer(at: t)
    let pressed = clicks.contains { t >= $0 && t < $0 + 0.12 }

    // The menu bar, while the menu bar window or the call is in use.
    let barAlpha = max(window(t, 2.4, 14.1, fade: 0.3), window(t, 36.6, 50.9, fade: 0.3), window(t, 54.9, 58.8, fade: 0.3))
    c.menuBar(alpha: barAlpha, recording: between(t, 4.2, 13.6), pressed: between(t, 5.3, 5.45) || between(t, 37.4, 37.55))
    c.notification(alpha: window(t, 2.9, 4.65, fade: 0.3), slide: (1 - ease((t - 2.9) / 0.35)) * 80 + ease((t - 4.35) / 0.3) * 80,
                   hover: between(t, 3.8, 4.3))

    // The detached window, pinned, then stopped.
    c.appWindow(frame: dFrame, scale: dScale, title: "Hesper", alpha: window(t, 10.7, 13.9, fade: 0.3), lift: (1 - ease((t - 10.7) / 0.35)) * 30) { c, p in
        let k = ease((t - 12.0) / 0.18)
        c.image(Img.det, in: p.rect(CGRect(x: 0, y: 0, width: 906, height: 1264)))
        if k > 0 { c.image(Img.detpin, in: p.rect(CGRect(x: 0, y: 0, width: 906, height: 1264)), alpha: k) }
    }

    // The meeting window: notes, where it was said, the speakers, scrolling; then the e-mail draft and Reminders.
    let scroll = 3478 * easeInOut((t - 25.2) / 2.7)
    let speakers = min(ease((t - 21.35) / 0.2), 1 - ease((t - 25.0) / 0.2))
    c.appWindow(frame: mFrame, scale: mScale, title: "Q4 Renewal with Northwind", alpha: window(t, 14.0, 36.6, fade: 0.4),
                lift: (1 - ease((t - 14.0) / 0.45)) * 40) { c, p in
        c.meetingPage(p, scroll: scroll, whereOpen: ease((t - 17.65) / 0.2))
        c.ring(p.rect(M.whereBlock.offsetBy(dx: 0, dy: -scroll)), alpha: window(t, 17.9, 20.8, fade: 0.4) * CGFloat(0.75 + 0.25 * sin(t * 4)) * (1 - speakers))
        if speakers > 0 {
            c.image(Img.speakers, in: p.rect(CGRect(x: 0, y: 0, width: 1120, height: 1360)), alpha: speakers)
            c.ring(p.rect(M.remembers), alpha: window(t, 21.6, 23.0, fade: 0.3))
            if t >= 22.3 && t < 25.2 {
                c.cover(CGRect(x: 276, y: 202, width: 560, height: 30), place: p, color: nameFill)
                let n = max(0, Int((t - nameTyped.start) * nameTyped.cps))
                c.typed(String(nameTyped.text.prefix(n)), place: p, left: 288, centerY: 216, font: fieldFont, lineHeight: 33, t: t)
            }
        }
        c.toast("Writing the e-mail…", center: p.at(CGPoint(x: 560, y: 1240)), alpha: window(t, 29.85, 31.0, fade: 0.2), scale: mScale)
        c.toast("Added 4 to-dos to Reminders.", center: p.at(CGPoint(x: 560, y: 1240)), alpha: window(t, 34.65, 36.3, fade: 0.2), scale: mScale)
    }
    let saved = mContent.at(M.saveNames)
    c.callout("Lisa Moreau's voice is saved", "She's named automatically in your next meetings.",
              frame: CGRect(x: saved.x - 640, y: saved.y - 150, width: 700, height: 100), alpha: window(t, 23.7, 25.1, fade: 0.25))
    c.menu(["The notes", "A follow-up e-mail, written for you"], topRight: mContent.at(CGPoint(x: 975, y: 1130)), width: 560 * mScale,
           scale: mScale, alpha: window(t, 28.85, 29.85, fade: 0.12), pointer: ptr)
    c.menu(["Open notes", "Open transcript", "Show in Finder", "-", "Name the speakers…", "Email the notes…", "Write a follow-up e-mail…",
            "Add my to-dos to Reminders", "Add all to-dos to Reminders", "Send to…"],
           topRight: mContent.at(CGPoint(x: 1095, y: 62)), width: 560 * mScale, scale: mScale, alpha: window(t, 33.75, 34.65, fade: 0.12), pointer: ptr)
    c.email(followUp, frame: CGRect(x: 950, y: 120 + (1 - ease((t - 30.8) / 0.35)) * 40, width: 860, height: 830),
            alpha: window(t, 30.8, 33.0, fade: 0.35), shown: ease((t - 31.1) / 1.5))

    // Settings → AI models.
    c.appWindow(frame: sFrame, scale: sScale, title: "Hesper Settings", alpha: window(t, 51.0, 55.0, fade: 0.4),
                lift: (1 - ease((t - 51.0) / 0.45)) * 30) { c, p in
        c.image(Img.models, in: p.rect(CGRect(x: 0, y: 0, width: 1560, height: 1080)))
        c.ring(p.rect(S.thisMac), alpha: window(t, 51.7, 53.1, fade: 0.3))
        c.ring(p.rect(S.recommended), alpha: window(t, 53.1, 54.8, fade: 0.3))
    }

    // The menu bar window.
    let popAlpha = popShown.map { window(t, $0.0, $0.1, fade: 0.25) }.max() ?? 0
    let opened = popShown.last { t >= $0.0 }?.0 ?? 0
    let drop = (1 - ease((t - opened) / 0.25)) * 14  // it drops in from the menu bar each time it opens
    c.popover(t: t, alpha: popAlpha, drop: drop) { c in
        if t >= notesTyped.start && t < 9.9 {
            let n = Int((t - notesTyped.start) * notesTyped.cps)
            c.typed(String(notesTyped.text.prefix(n)), place: pop, left: 70, centerY: 397, font: notesFont, lineHeight: 33, t: t)
        }
        if t >= 38.4 && t < 40.1 {  // the search box: the placeholder gives way to the question
            c.cover(CGRect(x: 80, y: 1003, width: 600, height: 38), place: pop, color: searchFill)
            let n = max(0, Int((t - askTyped.start) * askTyped.cps))
            c.typed(String(askTyped.text.prefix(n)), place: pop, left: 85, centerY: 1022, font: fieldFont, lineHeight: 33, t: t)
        }
        if t >= 44.4 && t < 44.95 { c.tick(P.vendor, place: pop, k: ease((t - 44.4) / 0.2)) }
    }
    c.menu(["Transcribe an audio file…", "Import a transcript…", "Open notes folder", "This week", "-", "Quit Hesper"],
           topRight: pop.at(CGPoint(x: 735, y: 72)), width: 520 * pop.scale, scale: pop.scale, alpha: window(t, 47.55, 48.3, fade: 0.1),
           pointer: ptr)

    // The pointer, on top of everything.
    for k in clicks { c.ripple(ptr, age: t - k) }
    c.pointer(ptr, pressed: pressed, alpha: window(t, pointerShown.0, pointerShown.1, fade: 0.3))
}

// MARK: - Encoding

try? FileManager.default.removeItem(at: out)
let writer = try! AVAssetWriter(outputURL: out, fileType: .mp4)
let input = AVAssetWriterInput(mediaType: .video, outputSettings: [
    AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: outW, AVVideoHeightKey: outH,
    AVVideoScalingModeKey: AVVideoScalingModeResizeAspect,
    AVVideoCompressionPropertiesKey: [AVVideoAverageBitRateKey: bitrate, AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
                                      AVVideoMaxKeyFrameIntervalDurationKey: 10, AVVideoAllowFrameReorderingKey: true,
                                      AVVideoExpectedSourceFrameRateKey: FPS] as [String: Any],
])
let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: input, sourcePixelBufferAttributes: [
    kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA, kCVPixelBufferWidthKey as String: W, kCVPixelBufferHeightKey as String: H,
])
writer.add(input)
writer.startWriting()
writer.startSession(atSourceTime: .zero)
let frames = Int(total * Double(FPS))
let posterFrame = Int(18.3 * Double(FPS))  // "See where it was said"
for i in 0..<frames {
    var pb: CVPixelBuffer?
    CVPixelBufferPoolCreatePixelBuffer(nil, adaptor.pixelBufferPool!, &pb)
    let buffer = pb!
    CVPixelBufferLockBaseAddress(buffer, [])
    let ctx = CGContext(data: CVPixelBufferGetBaseAddress(buffer), width: W, height: H, bitsPerComponent: 8,
                        bytesPerRow: CVPixelBufferGetBytesPerRow(buffer), space: sRGB,
                        bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)!
    frame(Canvas(ctx), t: Double(i) / Double(FPS))
    if let posterOut, i == posterFrame, let still = ctx.makeImage() {
        try? NSBitmapImageRep(cgImage: still).representation(using: .png, properties: [:])?.write(to: posterOut)
    }
    CVPixelBufferUnlockBaseAddress(buffer, [])
    while !input.isReadyForMoreMediaData { usleep(2000) }
    adaptor.append(buffer, withPresentationTime: CMTime(value: CMTimeValue(i), timescale: CMTimeScale(FPS)))
}
input.markAsFinished()
let done = DispatchSemaphore(value: 0)
writer.finishWriting { done.signal() }
done.wait()
print(writer.status == .completed ? "✅ \(out.path) — \(frames) frames, \(total) s" : "❌ \(writer.error?.localizedDescription ?? "?")")
