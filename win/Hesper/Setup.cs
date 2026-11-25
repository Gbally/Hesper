using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Hesper;

/// First launch (and after an update): everything into %LOCALAPPDATA%\Hesper (~/.local/share/Hesper
/// on Linux), no admin rights. The engine's source comes inside the app; uv, llama.cpp and the models
/// are downloaded (uv and llama.cpp at the versions tested, checked by their SHA-256); uv brings
/// Python 3.12. Shared with the Linux app (linux/Hesper).
static class Setup
{
    const string UvUrl = "https://github.com/astral-sh/uv/releases/download/0.13.6/uv-x86_64-pc-windows-msvc.zip";
    const string UvSha = "a2a8820d5eea32f08ef467ef3b7f66883a9b076d5737a2fc2fbb5f458d1bb50f";
    const string Llama = "https://github.com/ggml-org/llama.cpp/releases/download/b12532/";
    static readonly (string File, string Sha)[] LlamaCuda =
    [
        ("llama-b12532-bin-win-cuda-12.4-x64.zip", "b7a05a4d20fdd3336746fa1cbf08f3e7f26030593a4e419d7a378757e3d1d213"),
        ("cudart-llama-bin-win-cuda-12.4-x64.zip", "8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6"),
    ];
    static readonly (string File, string Sha)[] LlamaCpu =
        [("llama-b12532-bin-win-cpu-x64.zip", "beb8c051a782fb0970d4ae20fa276a6dfd3e7245685b185244a22570e9e473fa")];

    // Linux (x64 or arm64): uv's and llama.cpp's Ubuntu builds (they run on other distributions too).
    // llama.cpp: the Vulkan build with an NVIDIA GPU (its driver brings Vulkan; no 600 MB of CUDA
    // libraries), else the CPU one.
    static bool Arm => RuntimeInformation.OSArchitecture == Architecture.Arm64;
    static (string Url, string Sha) UvLinux => Arm
        ? ("https://github.com/astral-sh/uv/releases/download/0.13.6/uv-aarch64-unknown-linux-gnu.tar.gz", "229e194255c7958d86d56e8790d969a6f74736592133f0dfd0076f754c642c1d")
        : ("https://github.com/astral-sh/uv/releases/download/0.13.6/uv-x86_64-unknown-linux-gnu.tar.gz", "f29c3e94ffabd7e6f3ba521dba169a9b7ec12100bf3257087f0f24f6123f3645");
    static (string File, string Sha)[] LlamaLinux(bool gpu) => (gpu, Arm) switch
    {
        (true, false) => [("llama-b12532-bin-ubuntu-vulkan-x64.tar.gz", "7e5351db758c02adb35c013e7b63841d508d6b8d164502bf114682a10addd8a4")],
        (false, false) => [("llama-b12532-bin-ubuntu-x64.tar.gz", "887205c688f0f1a8829d2b74be1a1c20978c2afe45201a2659a7bea213220d15")],
        (true, true) => [("llama-b12532-bin-ubuntu-vulkan-arm64.tar.gz", "e97b8d42bdb9029d0772fc834d6bc5d69e8f34885bad270bd033b7edf601599e")],
        (false, true) => [("llama-b12532-bin-ubuntu-arm64.tar.gz", "47650d39565de64a76aa78e54269b70f71bde1d5139f46b32f4eb5212da2a823")],
    };

    static string Marker => Path.Combine(Paths.Home, "installed-version");
    static string LlamaDir => Path.Combine(Paths.Home, "models", "llama.cpp");
    public static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";

    /// The version and the engine inside this exe: a build with another engine (even at the same
    /// version, as test builds are) sets up again, so the engine's files are never older than the app.
    static string Stamp => $"{AppVersion} {EngineHash}";
    static string? engineHash;
    static string EngineHash
    {
        get
        {
            if (engineHash != null) return engineHash;
            using var zip = Assembly.GetExecutingAssembly().GetManifestResourceStream("engine.zip");
            return engineHash = zip == null ? "none" : Convert.ToHexString(SHA256.HashData(zip))[..16].ToLowerInvariant();
        }
    }

    public static bool IsInstalled =>
        File.Exists(Paths.Python) && File.Exists(Marker) && File.ReadAllText(Marker).Trim() == Stamp;

    /// Set up before, by this version or another one: an update (or a new engine) only needs the
    /// setup again, quietly, not the welcome window.
    public static bool WasInstalled => File.Exists(Paths.Python) && File.Exists(Marker);

    /// An NVIDIA GPU (its driver installs nvidia-smi): the CUDA builds then, else the CPU ones.
    public static bool HasNvidia => OperatingSystem.IsWindows()
        ? File.Exists(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"))
        : File.Exists("/proc/driver/nvidia/version") || Hardware.OnPath("nvidia-smi") != null;

    /// Linux: Vulkan's loader is there (llama.cpp's GPU build needs it; NVIDIA's driver uses it).
    static bool HasVulkan
    {
        get
        {
            if (!NativeLibrary.TryLoad("libvulkan.so.1", out var h)) return false;
            NativeLibrary.Free(h);
            return true;
        }
    }

    /// Runs every step; report(status, fraction 0..1 or null). Throws with a readable message.
    /// modelsThere: the models in use are already downloaded (an update): no need to ask Hugging Face
    /// again, which also lets an update set up without the internet.
    public static async Task Run(Action<string, double?> report, CancellationToken cancel, bool modelsThere = false)
    {
        Directory.CreateDirectory(Paths.Home);
        var gpu = HasNvidia;
        Paths.WriteLog($"setup {Stamp}, NVIDIA GPU: {gpu}");

        report("Unpacking the engine…", null);
        UnpackEngine();

        if (!File.Exists(Paths.Uv))
        {
            report("Downloading uv…", 0);
            var (url, sha) = OperatingSystem.IsWindows() ? (UvUrl, UvSha) : UvLinux;
            await Fetch(url, sha, Path.GetDirectoryName(Paths.Uv)!, f => report("Downloading uv…", f), cancel, find: Path.GetFileName(Paths.Uv));
        }

        report("Installing Python and the engine's packages (a few minutes)…", null);
        var sync = await EngineRunner.Run(Paths.Uv, ["sync", "--frozen", "--python", "3.12", "--project", Paths.Engine, .. gpu ? ["--extra", "cuda"] : Array.Empty<string>()],
                                          null, cancel);
        if (!sync.Ok) throw new InvalidOperationException("Installing the engine failed:\n" + sync.ErrorTail);

        if (!OperatingSystem.IsWindows() && gpu && !HasVulkan)
            Paths.WriteLog("NVIDIA GPU but no Vulkan loader (libvulkan.so.1): llama.cpp on the CPU");
        var llamaGpu = gpu && (OperatingSystem.IsWindows() || HasVulkan);
        var llama = OperatingSystem.IsWindows() ? gpu ? LlamaCuda : LlamaCpu : LlamaLinux(llamaGpu);
        var stamp = Path.Combine(LlamaDir, "build.txt");
        var want = string.Join(",", llama.Select(l => l.File));
        if (!File.Exists(stamp) || File.ReadAllText(stamp) != want)
        {
            if (Directory.Exists(LlamaDir)) Directory.Delete(LlamaDir, true);
            for (var i = 0; i < llama.Length; i++)
            {
                var label = $"Downloading llama.cpp ({(llamaGpu ? "NVIDIA GPU" : "CPU")})…";
                var (k, n) = (i, llama.Length);
                await Fetch(Llama + llama[i].File, llama[i].Sha, LlamaDir, f => report(label, (k + f) / n), cancel,
                            find: OperatingSystem.IsWindows() ? null : "llama-server");
            }
            File.WriteAllText(stamp, want);
        }

        if (!modelsThere)
        {
            report("Downloading the models…", 0);
            string step = "Downloading the models…";
            var models = await EngineRunner.Hesper(["setup"], line =>
            {
                var (s, p) = EngineRunner.Parse(line);
                if (s != null) step = s;
                if (s != null || p != null) report(step, p);
            }, cancel);
            if (!models.Ok) throw new InvalidOperationException("Downloading the models failed:\n" + models.ErrorTail);
        }

        File.WriteAllText(Marker, Stamp);
        report("Ready", 1);
    }

    /// The engine's source (src/hesper, pyproject.toml, uv.lock), embedded in the exe as engine.zip.
    static void UnpackEngine()
    {
        using var zip = Assembly.GetExecutingAssembly().GetManifestResourceStream("engine.zip")
            ?? throw new InvalidOperationException("This build has no engine inside.");
        var src = Path.Combine(Paths.Engine, "src");
        if (Directory.Exists(src)) Directory.Delete(src, true);  // files removed in this version mustn't linger
        ZipFile.ExtractToDirectory(zip, Paths.Engine, overwriteFiles: true);
    }

    /// Downloads a zip (or a .tar.gz), checks its SHA-256, unpacks it into dest. find: a file that must
    /// end up right in dest — the archive's folders above it are dropped (Linux's tarballs have one).
    static async Task Fetch(string url, string sha256, string dest, Action<double> progress, CancellationToken cancel, string? find = null)
    {
        var tmp = Path.Combine(Paths.Home, "download.part");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            var size = response.Content.Headers.ContentLength ?? 0;
            await using var from = await response.Content.ReadAsStreamAsync(cancel);
            await using var to = File.Create(tmp);
            var buffer = new byte[1 << 20];
            long got = 0;
            int n;
            while ((n = await from.ReadAsync(buffer, cancel)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, n), cancel);
                got += n;
                if (size > 0) progress((double)got / size);
            }
        }
        string actual;
        await using (var f = File.OpenRead(tmp))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(f, cancel)).ToLowerInvariant();
        if (actual != sha256)
        {
            File.Delete(tmp);
            throw new InvalidOperationException($"{Path.GetFileName(url)} isn't the expected file (SHA-256 {actual}).");
        }
        Directory.CreateDirectory(dest);
        if (url.EndsWith(".tar.gz", StringComparison.Ordinal))
        {
            await using (var file = File.OpenRead(tmp))
            await using (var gz = new GZipStream(file, CompressionMode.Decompress))
                await TarFile.ExtractToDirectoryAsync(gz, dest, overwriteFiles: true, cancel);  // keeps the files' modes (executables)
        }
        else ZipFile.ExtractToDirectory(tmp, dest, overwriteFiles: true);
        File.Delete(tmp);
        if (find != null) Flatten(dest, find);
    }

    /// Moves what's beside `file` (found somewhere under dest) up into dest itself.
    static void Flatten(string dest, string file)
    {
        if (File.Exists(Path.Combine(dest, file))) return;
        var found = Directory.EnumerateFiles(dest, file, SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidOperationException($"{file} isn't in the download.");
        var from = Path.GetDirectoryName(found)!;
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var to = Path.Combine(dest, Path.GetFileName(entry));
            if (Directory.Exists(entry)) Directory.Move(entry, to); else File.Move(entry, to, overwrite: true);
        }
        // the archive's own folders, now empty
        for (var d = from; d != null && Path.GetFullPath(d) != Path.GetFullPath(dest); d = Path.GetDirectoryName(d))
            if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
    }
}
