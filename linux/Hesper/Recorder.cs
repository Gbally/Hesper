using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Hesper;

/// One recording: the microphone and what the computer plays (the default output's "monitor") into
/// mic.wav and system.wav, 16 kHz mono 16-bit, on one timeline, as the Windows app's Recorder and the
/// Mac's mic.caf / system.caf. Through PulseAudio's simple API, which PipeWire also answers
/// (pipewire-pulse): the sound server converts to 16 kHz mono itself.
sealed class Recorder : IDisposable
{
    public string Folder { get; }
    public DateTime Started { get; private set; }
    public List<string> Notices { get; } = [];

    readonly Stopwatch clock = new();
    Track? mic, system;
    readonly string micName, speakerName;

    /// micName / speakerName: a device's description (Settings), or "" for the system's default.
    /// appOnly ("teams") isn't possible on Linux: everything the computer plays is recorded.
    public Recorder(string folder, string micName = "", string speakerName = "", string appOnly = "")
    {
        Folder = folder;
        this.micName = micName;
        this.speakerName = speakerName;
        if (appOnly.Length > 0) Notices.Add("Recording one app only isn't possible on Linux — recording all the computer's audio instead.");
    }

    /// The microphones (input) or the outputs whose sound can be recorded, for Settings.
    public static List<string> DeviceNames(bool input) =>
        (input ? Pulse.Sources() : Pulse.Sinks()).Select(d => d.Description).Distinct().ToList();

    /// Throws only when nothing at all can be recorded.
    public void Start()
    {
        Directory.CreateDirectory(Folder);
        Started = DateTime.Now;
        clock.Start();
        try
        {
            // The chosen output's monitor, else the default output's ("@DEFAULT_MONITOR@")
            var sink = speakerName.Length > 0 ? Pulse.Sinks().FirstOrDefault(d => d.Description.Contains(speakerName, StringComparison.OrdinalIgnoreCase)) : null;
            var device = sink != null ? sink.Name + ".monitor" : "@DEFAULT_MONITOR@";
            Paths.WriteLog($"recording the computer's sound from {sink?.Description ?? "the default output"}");
            system = new Track(device, "Meeting audio", Path.Combine(Folder, "system.wav"), Recorded);
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException)
        {
            Notices.Add($"The computer's sound can't be recorded ({e.Message}): only your microphone is.");
        }
        try
        {
            var source = micName.Length > 0 ? Pulse.Sources().FirstOrDefault(d => d.Description.Contains(micName, StringComparison.OrdinalIgnoreCase)) : null;
            Paths.WriteLog($"recording the microphone {source?.Description ?? "(the default one)"}");
            mic = new Track(source?.Name, "Microphone", Path.Combine(Folder, "mic.wav"), Recorded);
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException)
        {
            Notices.Add($"Microphone: {e.Message}");
        }
        if (mic == null && system == null)
            throw new InvalidOperationException(string.Join("\n", Notices));
        File.WriteAllText(Path.Combine(Folder, "meeting.json"),
            $"{{\"title\": \"\", \"started\": \"{Started:yyyy-MM-ddTHH:mm:ss}\"}}");
        system?.Start();
        mic?.Start();
    }

    // Paused: nothing is written, and the time spent paused is left out of the timeline (both tracks
    // shift by it, so they stay aligned and the transcript has no gap), as on the Mac and Windows.
    readonly object pauseLock = new();
    double pausedTotal;
    double? pausedSince;

    /// Seconds recorded so far (pauses left out): the position in the audio files.
    double Recorded()
    {
        lock (pauseLock)
        {
            var now = clock.Elapsed.TotalSeconds;
            return now - pausedTotal - (pausedSince is double p ? now - p : 0);
        }
    }

    public TimeSpan Elapsed => TimeSpan.FromSeconds(Recorded());

    public bool Paused { get { lock (pauseLock) return pausedSince != null; } }

    public void Pause()
    {
        lock (pauseLock)
        {
            if (pausedSince != null) return;
            pausedSince = clock.Elapsed.TotalSeconds;
        }
        foreach (var t in new[] { mic, system }) if (t != null) t.Paused = true;
    }

    public void Resume()
    {
        lock (pauseLock)
        {
            if (pausedSince is not double p) return;
            pausedTotal += clock.Elapsed.TotalSeconds - p;
            pausedSince = null;
        }
        foreach (var t in new[] { mic, system }) if (t != null) t.Paused = false;
    }

    public float? MicLevel => mic?.Level;
    public float? SystemLevel => system?.Level;

    /// When something was last heard, in seconds since the start (to notice a forgotten recording).
    public double LastSound => Math.Max(mic?.LastSound ?? 0, system?.LastSound ?? 0);

    /// After Stop: drops the audio after that many seconds (the quiet tail of a forgotten recording).
    public void Trim(double seconds)
    {
        foreach (var name in new[] { "mic.wav", "system.wav" })
        {
            var path = Path.Combine(Folder, name);
            if (File.Exists(path)) Wav.Trim(path, seconds);
        }
    }

    public async Task StopAsync()
    {
        await Task.WhenAll(mic?.StopAsync() ?? Task.CompletedTask, system?.StopAsync() ?? Task.CompletedTask);
        clock.Stop();
    }

    /// For the console test (--record).
    public void Stop() => StopAsync().GetAwaiter().GetResult();

    public void Dispose()
    {
        mic?.Dispose();
        system?.Dispose();
    }

    /// One recording stream → a 16 kHz mono WAV, read on a thread of its own a tenth of a second at a
    /// time. Its first sound is placed at its time since the recording started; if the sound server
    /// sends nothing for a while (an output asleep), the gap is filled with silence.
    sealed class Track : IDisposable
    {
        const int Rate = Wav.Rate, Chunk = Rate / 10;  // samples
        readonly IntPtr stream;
        readonly Wav.Writer writer;
        readonly Func<double> recorded;
        readonly Thread thread;
        volatile bool stopping;
        readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long written;  // samples
        public volatile float Level;
        public volatile bool Paused;
        public double LastSound;

        public Track(string? device, string name, string path, Func<double> recorded)
        {
            this.recorded = recorded;
            var spec = new Native.PaSampleSpec { Format = Native.PaSampleS16Le, Rate = Rate, Channels = 1 };
            var attr = new Native.PaBufferAttr { MaxLength = uint.MaxValue, TLength = uint.MaxValue, PreBuf = uint.MaxValue, MinReq = uint.MaxValue, FragSize = Chunk * 2 };
            stream = Native.pa_simple_new(null, "Hesper", Native.PaStreamRecord, device, name, ref spec, IntPtr.Zero, ref attr, out var error);
            if (stream == IntPtr.Zero) throw new InvalidOperationException(Native.PulseError(error));
            writer = new Wav.Writer(path);
            thread = new Thread(Read) { IsBackground = true, Name = name };
        }

        public void Start() => thread.Start();

        unsafe void Read()
        {
            var buffer = new byte[Chunk * 2];
            var samples = new short[Chunk];
            var first = true;
            try
            {
                while (!stopping)
                {
                    int error;
                    fixed (byte* p = buffer)
                        if (Native.pa_simple_read(stream, p, (nuint)buffer.Length, out error) < 0)
                        {
                            Paths.WriteLog($"{Path.GetFileName(writer.Path)}: {Native.PulseError(error)}");
                            break;
                        }
                    if (Paused) { Level = 0; continue; }  // nothing written while paused
                    Buffer.BlockCopy(buffer, 0, samples, 0, buffer.Length);
                    var due = (long)(recorded() * Rate);
                    if (first)
                    {
                        first = false;
                        var lead = due - Chunk;  // silence before this stream's first sound
                        if (lead > 0) { writer.WriteSilence(lead); written += lead; }
                    }
                    else if (due - Chunk - written > Rate)  // the server sent nothing for over a second (an output asleep): silence instead
                    {
                        var pad = due - Chunk - written;
                        writer.WriteSilence(pad);
                        written += pad;
                    }
                    writer.Write(samples, Chunk);
                    written += Chunk;
                    double sum = 0;
                    foreach (var s in samples) sum += (double)s * s;
                    Level = (float)(Math.Sqrt(sum / Chunk) / 32768);
                    if (Level > 0.01f) LastSound = (double)written / Rate;  // about -40 dB: speech, not room noise
                }
            }
            catch (Exception e) { Paths.WriteLog($"{Path.GetFileName(writer.Path)}: {e.Message}"); }
            finally { stopped.TrySetResult(); }
        }

        public async Task StopAsync()
        {
            stopping = true;
            if (thread.IsAlive) await Task.WhenAny(stopped.Task, Task.Delay(3000));
            writer.Flush();
        }

        public void Dispose()
        {
            stopping = true;
            if (thread.IsAlive) thread.Join(3000);
            writer.Dispose();
            if (!thread.IsAlive) Native.pa_simple_free(stream);
        }
    }
}

/// The sound devices, as the sound server lists them (`pactl`, from pulseaudio-utils, which PipeWire
/// systems use too), and the streams recording right now.
static class Pulse
{
    public record Device(string Name, string Description);

    public static List<Device> Sources() => Devices("sources").Where(d => !d.Name.EndsWith(".monitor")).ToList();
    public static List<Device> Sinks() => Devices("sinks");

    static List<Device> Devices(string kind)
    {
        var found = new List<Device>();
        string? name = null;
        foreach (var raw in (Pactl("list", kind) ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Name: ")) name = line[6..].Trim();
            else if (line.StartsWith("Description: ") && name != null) { found.Add(new(name, line[13..].Trim())); name = null; }
        }
        return found;
    }

    /// The apps recording now: (process id, program name) of each recording stream.
    public static List<(int Pid, string Binary)> Recording()
    {
        var found = new List<(int, string)>();
        int? pid = null;
        string? binary = null;
        void Flush() { if (binary != null) found.Add((pid ?? 0, binary)); pid = null; binary = null; }
        foreach (var raw in (Pactl("list", "source-outputs") ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Source Output #")) Flush();
            else if (Regex.Match(line, @"^application\.process\.id = ""(\d+)""") is { Success: true } m) pid = int.Parse(m.Groups[1].Value);
            else if (Regex.Match(line, @"^application\.process\.binary = ""(.+)""") is { Success: true } b) binary = b.Groups[1].Value;
        }
        Flush();
        return found;
    }

    public static bool HasPactl => Hardware.OnPath("pactl") != null;

    /// pactl's output, in English (its labels are parsed), or null.
    static string? Pactl(params string[] args)
    {
        if (!HasPactl) return null;
        try
        {
            var info = new ProcessStartInfo("pactl") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) info.ArgumentList.Add(a);
            info.Environment["LC_ALL"] = "C";
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }
}

/// 16 kHz mono 16-bit WAV files: written as the sound comes (the header's sizes kept right every few
/// seconds, so a crash loses little), cut, read back to listen to a moment.
static class Wav
{
    public const int Rate = 16_000;

    public sealed class Writer : IDisposable
    {
        public string Path { get; }
        readonly FileStream file;
        long dataBytes;
        DateTime lastHeader = DateTime.Now;

        public Writer(string path)
        {
            Path = path;
            file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            WriteHeader();
        }

        void WriteHeader()
        {
            var pos = file.Position;
            file.Position = 0;
            using (var w = new BinaryWriter(file, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                w.Write("RIFF"u8.ToArray()); w.Write((uint)(36 + dataBytes)); w.Write("WAVE"u8.ToArray());
                w.Write("fmt "u8.ToArray()); w.Write(16u); w.Write((ushort)1); w.Write((ushort)1);
                w.Write((uint)Rate); w.Write((uint)(Rate * 2)); w.Write((ushort)2); w.Write((ushort)16);
                w.Write("data"u8.ToArray()); w.Write((uint)dataBytes);
            }
            file.Position = Math.Max(pos, 44);
        }

        public void Write(short[] samples, int count)
        {
            var bytes = new byte[count * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            file.Write(bytes);
            dataBytes += bytes.Length;
            if (DateTime.Now - lastHeader > TimeSpan.FromSeconds(5)) Flush();
        }

        public void WriteSilence(long samples)
        {
            var zero = new byte[Math.Min(samples * 2, 1 << 16)];
            for (var left = samples * 2; left > 0; left -= zero.Length) file.Write(zero, 0, (int)Math.Min(zero.Length, left));
            dataBytes += samples * 2;
        }

        public void Flush()
        {
            lastHeader = DateTime.Now;
            WriteHeader();
            file.Flush();
        }

        public void Dispose()
        {
            Flush();
            file.Dispose();
        }
    }

    /// The samples from `start` seconds (any WAV this app or the Windows one wrote: 16 kHz mono 16-bit).
    public static short[] Read(string path, double start, double seconds)
    {
        using var f = File.OpenRead(path);
        var data = DataOffset(f);
        if (data < 0) return [];
        var from = data + (long)(start * Rate) * 2;
        var count = (int)Math.Max(0, Math.Min((long)(seconds * Rate), (f.Length - from) / 2));
        if (count == 0) return [];
        f.Position = from;
        var bytes = new byte[count * 2];
        f.ReadExactly(bytes);
        var samples = new short[count];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }

    /// Keeps the first `seconds` of the file.
    public static void Trim(string path, double seconds)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        var data = DataOffset(f);
        if (data < 0) return;
        var keep = Math.Min(f.Length - data, (long)(seconds * Rate) * 2);
        f.SetLength(data + keep);
        f.Position = 4; f.Write(BitConverter.GetBytes((uint)(data - 8 + keep)));
        f.Position = data - 4; f.Write(BitConverter.GetBytes((uint)keep));
    }

    /// Where the samples start (after the "data" chunk's header), or -1.
    static long DataOffset(FileStream f)
    {
        var head = new byte[12];
        if (f.Read(head) < 12) return -1;
        var chunk = new byte[8];
        while (f.Read(chunk) == 8)
        {
            var size = BitConverter.ToUInt32(chunk, 4);
            if (chunk[0] == 'd' && chunk[1] == 'a' && chunk[2] == 't' && chunk[3] == 'a') return f.Position;
            f.Position += size + (size & 1);
        }
        return -1;
    }
}
