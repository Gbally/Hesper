namespace Hesper;

/// Hesper's icons, drawn with cairo into PNG files: the app's (the Mac's: the evening star rising over
/// a planet's lit edge) for the menu entry, windows and notifications, and the tray's (as Windows':
/// the star in a rounded square; a red dot while recording).
static class Brand
{
    /// Writes hesper.png (256 px), hesper-tray.png and hesper-tray-recording.png (64 px) into dir,
    /// unless they're there.
    public static void WriteIcons(string dir, bool again = false)
    {
        Directory.CreateDirectory(dir);
        void Write(string name, int size, Action<IntPtr> draw)
        {
            var file = Path.Combine(dir, name);
            if (File.Exists(file) && !again) return;
            var surface = Native.cairo_image_surface_create(0 /* CAIRO_FORMAT_ARGB32 */, size, size);
            var cr = Native.cairo_create(surface);
            Native.cairo_scale(cr, size / 100.0, size / 100.0);  // drawn on a 100 × 100 grid
            draw(cr);
            Native.cairo_destroy(cr);
            Native.cairo_surface_write_to_png(surface, file);
            Native.cairo_surface_destroy(surface);
        }
        Write("hesper.png", 256, AppIcon);
        Write("hesper-tray.png", 64, cr => Tray(cr, false));
        Write("hesper-tray-recording.png", 64, cr => Tray(cr, true));
    }

    static void Rgb(IntPtr cr, uint hex, double alpha = 1) =>
        Native.cairo_set_source_rgba(cr, (hex >> 16 & 255) / 255.0, (hex >> 8 & 255) / 255.0, (hex & 255) / 255.0, alpha);

    static void RoundedSquare(IntPtr cr, double r)
    {
        Native.cairo_new_path(cr);
        Native.cairo_arc(cr, 100 - r, r, r, -Math.PI / 2, 0);
        Native.cairo_arc(cr, 100 - r, 100 - r, r, 0, Math.PI / 2);
        Native.cairo_arc(cr, r, 100 - r, r, Math.PI / 2, Math.PI);
        Native.cairo_arc(cr, r, r, r, Math.PI, 3 * Math.PI / 2);
        Native.cairo_close_path(cr);
    }

    /// The four-point star with curved sides: from each tip, a quadratic curve through the centre.
    static void Star(IntPtr cr, double top, double bottom, double left, double right, double cx)
    {
        var cy = (top + bottom) / 2;
        (double X, double Y)[] tips = [(cx, top), (right, cy), (cx, bottom), (left, cy), (cx, top)];
        Native.cairo_new_path(cr);
        Native.cairo_move_to(cr, tips[0].X, tips[0].Y);
        for (var i = 0; i < 4; i++)
        {
            var (a, b) = (tips[i], tips[i + 1]);
            Native.cairo_curve_to(cr, a.X + 2.0 / 3 * (cx - a.X), a.Y + 2.0 / 3 * (cy - a.Y),
                                      b.X + 2.0 / 3 * (cx - b.X), b.Y + 2.0 / 3 * (cy - b.Y), b.X, b.Y);
        }
        Native.cairo_close_path(cr);
    }

    static void AppIcon(IntPtr cr)
    {
        RoundedSquare(cr, 22);
        Rgb(cr, 0x1B1F2E);
        Native.cairo_fill(cr);
        // the planet and its lit edge, inside the square
        RoundedSquare(cr, 22);
        Native.cairo_clip(cr);
        Native.cairo_new_path(cr);
        Native.cairo_arc(cr, 50, 132, 64, 0, 2 * Math.PI);
        Rgb(cr, 0x2E3550);
        Native.cairo_fill_preserve(cr);
        Rgb(cr, 0xF2B544, 0.9);
        Native.cairo_set_line_width(cr, 2.2);
        Native.cairo_stroke(cr);
        Native.cairo_reset_clip(cr);
        Star(cr, 22, 54, 36, 64, 50);
        Rgb(cr, 0xF2B544);
        Native.cairo_fill(cr);
    }

    static void Tray(IntPtr cr, bool recording)
    {
        RoundedSquare(cr, 22);
        Rgb(cr, 0x2E3550);
        Native.cairo_fill(cr);
        Star(cr, 14, 66, 26, 74, 50);
        Rgb(cr, 0xF2B544);
        Native.cairo_fill(cr);
        if (!recording) return;
        Native.cairo_new_path(cr);
        Native.cairo_arc(cr, 78, 78, 20, 0, 2 * Math.PI);
        Rgb(cr, 0xD93A3A);
        Native.cairo_fill_preserve(cr);
        Rgb(cr, 0x2E3550);
        Native.cairo_set_line_width(cr, 6);
        Native.cairo_stroke(cr);
    }
}
