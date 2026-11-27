using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hesper;

// Shared with the Linux app (linux/Hesper): no Windows-only APIs here (they go through Platform, or
// the Windows half of a partial class in Services.cs; Linux has its own).

/// A meeting's notes as text, for an e-mail or the clipboard (Meeting.shareText on the Mac).
static partial class Share
{
    /// labels: the headings in the app's language ({"Summary": "Résumé", …}), from the page.
    public static string Text(JsonObject m, JsonObject? labels)
    {
        string tr(string k) => labels?[k]?.GetValue<string>() ?? k;
        var title = m["title"]?.GetValue<string>() is { Length: > 0 } t ? t : tr("Meeting");
        var n = m["notes"];
        var date = DateTime.TryParse(m["date"]?.GetValue<string>(), out var d) ? d.ToString("g") : "";
        var out_ = new List<string> { $"{title} — {date}", "" };
        void Bullets(string label, JsonNode? items)
        {
            if (items is not JsonArray a || a.Count == 0) return;
            out_.Add(tr(label));
            out_.AddRange(a.Select(i => "• " + i));
            out_.Add("");
        }
        Bullets("Summary", n?["summary"]);
        Bullets("Decisions", n?["decisions"]);
        if (n?["actions"] is JsonArray acts && acts.Count > 0)
        {
            out_.Add(tr("Action items"));
            foreach (var a in acts)
            {
                var extra = new[] { a?["owner"]?.GetValue<string>(), a?["due"]?.GetValue<string>() }.Where(s => !string.IsNullOrEmpty(s));
                out_.Add($"☐ {a?["task"]}" + (extra.Any() ? $" ({string.Join(", ", extra)})" : ""));
            }
            out_.Add("");
        }
        Bullets("Open questions", n?["questions"]);
        return string.Join("\n", out_).TrimEnd();
    }

    /// A draft in the chosen mail app: the default one (mailto), Outlook, Gmail or Outlook on the web.
    /// A draft to the meeting's attendees (from the calendar), as the Mac's.
    public static void Email(string app, string gmailAccount, IReadOnlyList<string> to, string subject, string body)
    {
        string Q(string s) => Uri.EscapeDataString(s);
        var list = string.Join(",", to);
        switch (app)
        {
            case "gmail":
                var account = gmailAccount.Trim().Length > 0 ? $"authuser={Q(gmailAccount.Trim())}&" : "";
                Open($"https://mail.google.com/mail/?{account}view=cm&fs=1&to={Q(list)}&su={Q(subject)}&body={Q(body)}");
                break;
            case "outlookWeb":
                Open($"https://outlook.office.com/mail/deeplink/compose?to={Q(list)}&subject={Q(subject)}&body={Q(body)}");
                break;
            case "outlook" when OutlookPath() is { } outlook:
                Process.Start(new ProcessStartInfo(outlook) { ArgumentList = { "/c", "ipm.note", "/m", $"{string.Join(";", to)}?subject={Q(subject)}&body={Q(body)}" } });
                break;
            default:
                Open($"mailto:{list}?subject={Q(subject)}&body={Q(body)}");
                break;
        }
    }

    /// The attendees' addresses of a meeting from the calendar (meeting.json), for an e-mail.
    public static List<string> Attendees(JsonObject m) =>
        (m["calendar"]?["attendees"] as JsonArray ?? []).Select(a => a?["email"]?.GetValue<string>()?.Trim() ?? "")
            .Where(e => e.Contains('@') && !e.Any(c => c is ',' or ';' or '?' or '&' or ' ')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

/// The notes as Markdown for a notes app's folder (Obsidian…), as mac/App/Export.swift writes them.
static class Export
{
    public static string? Markdown(string folder, JsonObject m)
    {
        var notes = Path.Combine(folder, "notes.md");
        if (!File.Exists(notes)) return null;
        var day = DateTime.TryParse(m["date"]?.GetValue<string>(), out var d) ? d.ToString("yyyy-MM-dd") : "";
        var people = (m["notes"]?["participants"] as JsonArray ?? []).Select(p => $"\"{p?.GetValue<string>().Replace("\"", "'")}\"");
        var front = new[] { "---", $"date: {day}", $"participants: [{string.Join(", ", people)}]", "tags: [meeting]",
                            "source: Hesper", $"hesper_id: \"{m["id"]}\"", "---", "" };
        var lines = File.ReadAllLines(notes).Where(l => !l.Contains("(transcript.md)"));  // only works in the meeting's folder
        return string.Join("\n", front.Concat(lines));
    }

    /// "2026-10-23 Weekly sync.md", or "… 2.md" when that name is another meeting's copy.
    public static string FileFor(JsonObject m, string dir)
    {
        var day = DateTime.TryParse(m["date"]?.GetValue<string>(), out var d) ? d.ToString("yyyy-MM-dd") : "";
        var title = Regex.Replace(m["title"]?.GetValue<string>() ?? "", @"[/\\:*?""<>|]", "-").Trim();
        var name = $"{day} {(title.Length > 0 ? title[..Math.Min(80, title.Length)] : "Meeting")}";
        var id = m["id"]!.GetValue<string>();
        for (var n = 1; n < 100; n++)
        {
            var file = Path.Combine(dir, n == 1 ? $"{name}.md" : $"{name} {n}.md");
            if (!File.Exists(file)) return file;
            var front = File.ReadLines(file).Skip(1).TakeWhile(l => l != "---");
            var owner = front.FirstOrDefault(l => l.StartsWith("hesper_id:") || l.StartsWith("meetscribe_id:"));
            if (owner == null || owner.Split(':', 2)[1].Trim(' ', '"', '\'') == id) return file;
        }
        return Path.Combine(dir, $"{name} {id}.md");
    }

    public static string? Save(string folder, JsonObject m, string dir)
    {
        if (Markdown(folder, m) is not { } text) return null;
        Directory.CreateDirectory(dir);
        var file = FileFor(m, dir);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        return file;
    }
}

/// Where the notes folder can be: this PC, a OneDrive account, another folder.
static partial class Storage
{
    public static string? SyncService(string folder)
    {
        foreach (var (name, path) in OneDriveAccounts())
            if (folder.StartsWith(path, StringComparison.OrdinalIgnoreCase)) return name;
        foreach (var s in new[] { "Dropbox", "Google Drive", "iCloudDrive", "Nextcloud", "Box" })
            if (folder.Contains(s, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    public static string FolderFor(AppSettings s) => s.Storage switch
    {
        "onedrive" when s.OneDrivePath.Length > 0 => Path.Combine(s.OneDrivePath, "MeetingNotes"),
        "custom" when s.CustomFolderPath.Length > 0 => s.CustomFolderPath,
        _ => Paths.DefaultNotesFolder(),
    };

    /// Moves the meeting folders from one notes folder to another; returns how many couldn't be.
    public static int Move(string from, string to)
    {
        Directory.CreateDirectory(to);
        var failed = 0;
        foreach (var dir in Directory.Exists(from) ? Directory.GetDirectories(from) : [])
        {
            var dest = Path.Combine(to, Path.GetFileName(dir));
            try
            {
                if (Directory.Exists(dest)) { failed++; continue; }
                MoveFolder(dir, dest);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed++; }
        }
        return failed;
    }

    /// One folder to another place, also on another drive (copied, then deleted).
    public static void MoveFolder(string dir, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (Path.GetPathRoot(Path.GetFullPath(dir))!.Equals(Path.GetPathRoot(Path.GetFullPath(dest)), StringComparison.OrdinalIgnoreCase))
            Directory.Move(dir, dest);
        else
        {
            Platform.CopyDirectory(dir, dest);
            Directory.Delete(dir, true);
        }
    }
}

/// Deletes the recordings (and voice samples) of processed meetings after the chosen number of days.
static class Retention
{
    static readonly string[] Audio = ["mic.wav", "system.wav", "mic.caf", "system.caf", "clips.json"];

    public static void DeleteAudio(string folder)
    {
        foreach (var f in Audio) File.Delete(Path.Combine(folder, f));
        var clips = Path.Combine(folder, "clips");
        if (Directory.Exists(clips)) Directory.Delete(clips, true);
    }

    public static int CleanUp(string notesFolder, int days, ICollection<string> busy)
    {
        if (days <= 0 || !Directory.Exists(notesFolder)) return 0;
        var limit = DateTime.Now.AddDays(-days);
        var n = 0;
        foreach (var dir in Directory.GetDirectories(notesFolder))
        {
            if (busy.Contains(dir) || !File.Exists(Path.Combine(dir, "notes.json")) || !Meetings.HasAudio(dir)) continue;
            if (Directory.GetCreationTime(dir) > limit) continue;
            DeleteAudio(dir);
            n++;
        }
        if (n > 0) Paths.WriteLog($"retention: deleted audio of {n} meeting(s) older than {days} days");
        return n;
    }
}

/// Self-update from the public releases repo: latest-windows.json / latest-linux.json (or -beta)
/// says which build is current; Install (Windows' exe, Linux's AppImage) is each platform's.
static partial class Updater
{
    const string Raw = "https://raw.githubusercontent.com/Gbally/Hesper/main/";
    /// This exe's build number (the 4th part of its version, set by scripts/release_windows.ps1),
    /// compared with the feed's; 0 for a build made by hand.
    public static int Build => Math.Max(0, typeof(Updater).Assembly.GetName().Version?.Revision ?? 0);

    public static async Task<JsonObject?> Check(bool testVersions)
    {
        if (Platform.Store || Paths.Demo) return null;  // the Microsoft Store updates it; no badge in the Store's pictures
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        async Task<JsonObject?> Get(string f)
        {
            try { return JsonNode.Parse(await http.GetStringAsync(Raw + f + "?t=" + DateTime.UtcNow.Ticks))?.AsObject(); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { return null; }
        }
        var release = await Get(Platform.Feed + ".json");
        var test = testVersions ? await Get(Platform.Feed + "-beta.json") : null;
        var best = new[] { release, test }.OfType<JsonObject>().MaxBy(i => i["build"]?.GetValue<int>() ?? 0);
        return best != null && (best["build"]?.GetValue<int>() ?? 0) > Build ? best : null;
    }
}

/// Settings → Privacy → Remove Hesper: the engine, models, settings and voices (all of Paths.Home),
/// the login item, optionally the notes (to the Recycle Bin / Trash), then the app (Run, each platform's).
static partial class Uninstall
{
    public static long Size(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }
}

/// A hesper:// link opened while Hesper runs starts a second Hesper: it hands the link over to the
/// running one through a named pipe (this user's session only; a local socket on Linux) and quits.
static class LinkPipe
{
    static string Name(string instance) => $"Hesper-links{instance}-{Environment.UserName}";

    public static bool Send(string instance, string url)
    {
        try
        {
            using var c = new System.IO.Pipes.NamedPipeClientStream(".", Name(instance), System.IO.Pipes.PipeDirection.Out);
            c.Connect(3000);
            using var w = new StreamWriter(c);
            w.Write(url);
            return true;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { return false; }
    }

    /// Calls got (on a thread of its own) with each link sent.
    public static void Listen(string instance, Action<string> got)
    {
        // Linux: the pipe is a socket file, left behind if Hesper crashed; this one is the only Hesper
        if (!OperatingSystem.IsWindows())
            try { File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + Name(instance))); } catch (IOException) { }
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new System.IO.Pipes.NamedPipeServerStream(Name(instance), System.IO.Pipes.PipeDirection.In);
                    s.WaitForConnection();
                    using var r = new StreamReader(s);
                    var url = r.ReadToEnd().Trim();
                    if (url.StartsWith("hesper:", StringComparison.OrdinalIgnoreCase)) got(url);
                }
                catch (IOException e) { Paths.WriteLog("links: " + e.Message); Thread.Sleep(1000); }
            }
        }) { IsBackground = true, Name = "hesper:// links" };
        t.Start();
    }
}
