using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Hesper;

/// "Open Hesper at login": a value in the user's Run key (no admin rights).
static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get => Registry.CurrentUser.OpenSubKey(Key)?.GetValue("Hesper") is string;
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (value) k.SetValue("Hesper", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("Hesper", false);
        }
    }
}

/// Windows: classic Outlook's exe, for e-mail drafts (Share.Email).
static partial class Share
{
    /// Classic Outlook's exe, from its App Paths entry.
    public static string? OutlookPath() =>
        Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE")?.GetValue("") as string;
}

/// Windows: the OneDrive accounts signed in on this PC.
static partial class Storage
{
    /// OneDrive accounts signed in on this PC (personal and work), as (name, folder).
    public static List<(string Name, string Path)> OneDriveAccounts()
    {
        var found = new List<(string, string)>();
        using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
        foreach (var name in accounts?.GetSubKeyNames() ?? [])
        {
            using var k = accounts!.OpenSubKey(name);
            if (k?.GetValue("UserFolder") is string folder && Directory.Exists(folder))
                found.Add((k.GetValue("DisplayName") as string is { Length: > 0 } dn ? $"OneDrive – {dn}" : Path.GetFileName(folder), folder));
        }
        return found.DistinctBy(a => a.Item2.ToLowerInvariant()).ToList();
    }
}

/// Windows: the new exe is downloaded, checked against its SHA-256, swapped in and started.
static partial class Updater
{
    /// Downloads and checks the new exe, then swaps it with this one and restarts.
    public static async Task Install(JsonObject info, Action<double> progress)
    {
        var url = info["url"]!.GetValue<string>();
        var sha = info["sha256"]!.GetValue<string>().ToLowerInvariant();
        if (!url.StartsWith("https://github.com/Gbally/Hesper/", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected download address.");
        var exe = Environment.ProcessPath!;
        var tmp = exe + ".new";
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
        using (var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            r.EnsureSuccessStatusCode();
            var size = r.Content.Headers.ContentLength ?? 0;
            await using var from = await r.Content.ReadAsStreamAsync();
            await using var to = File.Create(tmp);
            var buf = new byte[1 << 20];
            long got = 0;
            int n;
            while ((n = await from.ReadAsync(buf)) > 0) { await to.WriteAsync(buf.AsMemory(0, n)); got += n; if (size > 0) progress((double)got / size); }
        }
        string actual;
        await using (var f = File.OpenRead(tmp)) actual = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
        if (actual != sha) { File.Delete(tmp); throw new InvalidOperationException("The download isn't the expected file."); }
        var old = exe + ".old";
        File.Delete(old);
        File.Move(exe, old);   // a running exe can be renamed, not overwritten
        File.Move(tmp, exe);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, ArgumentList = { "--after-update" } });
    }

    /// The previous exe, left by an update.
    public static void CleanUp()
    {
        try { File.Delete(Environment.ProcessPath + ".old"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// Windows: the Run key, the app ID and the links go, the notes to the Recycle Bin if asked, then the
/// folder and the exe once Hesper has quit.
static partial class Uninstall
{
    public static void Run(string notesFolder, bool removeNotes)
    {
        Startup.Enabled = false;
        Identity.Remove();
        if (removeNotes && Directory.Exists(notesFolder))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(notesFolder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                                   Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        // After Hesper has quit: delete its folder and the exe (a running exe can't delete itself)
        var script = $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{Paths.Home}\" & del /f /q \"{Environment.ProcessPath}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", script) { CreateNoWindow = true, UseShellExecute = false });
    }
}
