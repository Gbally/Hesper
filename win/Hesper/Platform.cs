using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.VisualBasic.FileIO;

namespace Hesper;

/// What the shared code (AppModel, Common.cs…) asks of the system, the Windows way. The Linux app
/// (linux/Hesper/Platform.cs) has the same members.
static class Platform
{
    public const string Name = "windows";

    /// The update feeds: latest-windows.json and latest-windows-beta.json.
    public const string Feed = "latest-windows";

    /// The Microsoft Store version (an MSIX package, scripts/store_windows.ps1): Windows installs,
    /// updates and removes it, and gives it its app ID, Start menu entry, hesper:// links and login
    /// item (win/Store/AppxManifest.xml). False for the downloaded exe.
    public static bool Store { get; } = HasPackage();

    /// The Store version's data folder: the package's own (…\Packages\<name>\LocalCache), a real
    /// folder every app sees, deleted by Windows with the app. Null for the downloaded exe.
    public static string? StoreHome => Store ? global::Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path : null;

    [DllImport("kernel32.dll")]
    static extern int GetCurrentPackageFullName(ref int length, IntPtr name);

    static bool HasPackage()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != 15700;  // APPMODEL_ERROR_NO_PACKAGE
    }

    /// What the desktop allows, for the pages (Linux: a tray icon or not, global shortcuts…); nothing
    /// to say on Windows.
    public static JsonObject? Desktop() => null;

    public static void CopyText(string text) => Clipboard.SetText(text);

    public static string? ChooseFolder(string title)
    {
        using var dlg = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = title };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }

    /// A file of one of these types (extensions); anyFile: "*.*" offered too.
    public static string? ChooseFile(string title, string kind, string[] types, bool anyFile)
    {
        using var dlg = new OpenFileDialog
        {
            Title = title, Filter = $"{kind}|{string.Join(";", types.Select(t => "*." + t))}" + (anyFile ? "|*.*|*.*" : ""),
        };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    public static void ShowFolder(string folder) => Process.Start("explorer.exe", $"\"{folder}\"");

    /// The file's folder, with the file selected.
    public static void ShowFile(string file) => Process.Start("explorer.exe", $"/select,\"{file}\"");

    /// To the Recycle Bin (as the Mac's Trash).
    public static void Trash(string folder) => FileSystem.DeleteDirectory(folder, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    public static void CopyDirectory(string from, string to) => FileSystem.CopyDirectory(from, to);

    /// A moment marked: a short sound.
    public static void Ding() => System.Media.SystemSounds.Asterisk.Play();

    public static void OpenNotificationSettings() => Share.Open("ms-settings:notifications");

    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint flags);

    /// on: the PC doesn't go to sleep (the screen may still turn off). The setting is per thread:
    /// always called on the UI thread.
    public static void KeepAwake(bool on) =>
        SetThreadExecutionState(0x80000000u /* ES_CONTINUOUS */ | (on ? 0x1u /* ES_SYSTEM_REQUIRED */ : 0u));
}

/// A timer ticking on the UI thread: WinForms' here, GLib's on Linux.
sealed class UiTimer : System.Windows.Forms.Timer { }
