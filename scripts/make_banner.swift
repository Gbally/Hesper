// The banner for the README, GitHub's social preview and link previews of the site: the video's
// title card (icon, "Hesper", the tagline). Same palette and type as scripts/make_video.swift.
//
//   swift scripts/make_banner.swift OUT.png|OUT.jpg [WIDTH]      (2:1, default 1280 × 640)
//
// docs/img/banner.jpg (2560): the top of the README. docs/img/social-preview.png (1280): GitHub →
// Settings → Social preview (under 1 MB), and the site's link previews (og:image).

import AppKit

let args = CommandLine.arguments
let out = URL(fileURLWithPath: args.count > 1 ? args[1] : "banner.png")
let PW = args.count > 2 ? Int(args[2])! : 1280, PH = PW / 2

func rgb(_ hex: UInt32, _ a: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat(hex >> 16 & 255) / 255, green: CGFloat(hex >> 8 & 255) / 255, blue: CGFloat(hex & 255) / 255, alpha: a)
}
let duskInk = rgb(0x1B1F2E), horizon = rgb(0x2E3550), star = rgb(0xF2B544)
let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!

func rounded(_ size: CGFloat, _ weight: NSFont.Weight) -> NSFont {
    let base = NSFont.systemFont(ofSize: size, weight: weight)
    return NSFont(descriptor: base.fontDescriptor.withDesign(.rounded) ?? base.fontDescriptor, size: size) ?? base
}

// Drawn in the video's 1920 × 1080 units (top-left origin), centred in a 2:1 canvas: 2160 × 1080.
let U = CGFloat(PH) / 1080, W: CGFloat = 2160, H: CGFloat = 1080, dx: CGFloat = 120
let ctx = CGContext(data: nil, width: PW, height: PH, bitsPerComponent: 8, bytesPerRow: 0, space: sRGB,
                    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
ctx.scaleBy(x: U, y: U)
ctx.interpolationQuality = .high
ctx.setShouldAntialias(true)
func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x + dx, y: H - y) }  // video units → canvas
func flip(_ r: CGRect) -> CGRect { CGRect(x: r.minX + dx, y: H - r.maxY, width: r.width, height: r.height) }

// The dusk background, as in the video.
ctx.setFillColor(duskInk); ctx.fill(CGRect(x: 0, y: 0, width: W, height: H))
let bg = CGGradient(colorsSpace: sRGB, colors: [rgb(0x262B42), duskInk] as CFArray, locations: [0, 1])!
ctx.drawLinearGradient(bg, start: CGPoint(x: 0, y: H), end: CGPoint(x: W, y: 0), options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])

// The evening star's faint glow in the upper right, as in the video.
let glow = CGGradient(colorsSpace: sRGB, colors: [rgb(0xF2B544, 0.07), rgb(0xF2B544, 0)] as CFArray, locations: [0, 1])!
ctx.drawRadialGradient(glow, startCenter: CGPoint(x: 1620 + dx, y: 900), startRadius: 0, endCenter: CGPoint(x: 1620 + dx, y: 900), endRadius: 700, options: [])

// The app icon (the brand vectors, 100-unit box).
func icon(center: CGPoint, size: CGFloat) {
    let f = flip(CGRect(x: center.x - size / 2, y: center.y - size / 2, width: size, height: size)), u = size / 100
    func map(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: f.minX + x * u, y: f.maxY - y * u) }
    let square = CGPath(roundedRect: f, cornerWidth: 22 * u, cornerHeight: 22 * u, transform: nil)
    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -24), blur: 40, color: rgb(0x000000, 0.55))
    ctx.addPath(square); ctx.setFillColor(duskInk); ctx.fillPath()
    ctx.restoreGState()
    ctx.saveGState(); ctx.addPath(square); ctx.clip()
    let c = map(50, 132)
    let planet = CGRect(x: c.x - 64 * u, y: c.y - 64 * u, width: 128 * u, height: 128 * u)
    ctx.setFillColor(horizon); ctx.fillEllipse(in: planet)
    ctx.setStrokeColor(star.copy(alpha: 0.9)!); ctx.setLineWidth(2.2 * u); ctx.strokeEllipse(in: planet)
    ctx.restoreGState()
    let top = map(50, 22), right = map(64, 38), bottom = map(50, 54), left = map(36, 38), mid = map(50, 38)
    let s = CGMutablePath(); s.move(to: top)
    for (a, b) in [(top, right), (right, bottom), (bottom, left), (left, top)] {
        s.addCurve(to: b, control1: CGPoint(x: a.x + 2 / 3 * (mid.x - a.x), y: a.y + 2 / 3 * (mid.y - a.y)),
                   control2: CGPoint(x: b.x + 2 / 3 * (mid.x - b.x), y: b.y + 2 / 3 * (mid.y - b.y)))
    }
    s.closeSubpath()
    ctx.addPath(s); ctx.setFillColor(star); ctx.fillPath()
}

/// Centred text; *stars* mark the accent part.
func text(_ str: String, y: CGFloat, font: NSFont, color: CGColor, accent: CGColor? = nil) {
    let para = NSMutableParagraphStyle(); para.alignment = .center
    let attr = NSMutableAttributedString()
    for (i, part) in str.components(separatedBy: "*").enumerated() {
        attr.append(NSAttributedString(string: part, attributes: [
            .font: font, .foregroundColor: NSColor(cgColor: i % 2 == 1 ? accent ?? color : color)!, .paragraphStyle: para]))
    }
    let h = ceil(attr.boundingRect(with: CGSize(width: 1920, height: 400), options: [.usesLineFragmentOrigin]).height)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
    attr.draw(with: flip(CGRect(x: 0, y: y, width: 1920, height: h)), options: [.usesLineFragmentOrigin])
    NSGraphicsContext.restoreGraphicsState()
}

icon(center: CGPoint(x: 960, y: 330), size: 200)
text("Hesper", y: 470, font: rounded(118, .heavy), color: rgb(0xFFFFFF))
text("Private meeting notes, *fully offline*", y: 640, font: rounded(50, .semibold), color: rgb(0xFFFFFF), accent: star)

let rep = NSBitmapImageRep(cgImage: ctx.makeImage()!)
let jpeg = ["jpg", "jpeg"].contains(out.pathExtension.lowercased())
try! rep.representation(using: jpeg ? .jpeg : .png, properties: jpeg ? [.compressionFactor: 0.88] : [:])!.write(to: out)
print("\(out.path): \(PW) × \(PH)")
