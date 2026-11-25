namespace Hesper;

/// Plays a moment of a meeting (a marked one, or where a to-do was said): both tracks together, your
/// mic and the meeting, from a little before it. One at a time. As the Mac's and Windows' MomentPlayer.
sealed class MomentPlayer : IDisposable
{
    readonly UiTimer timer = new();
    CancellationTokenSource? playing;
    public string? Playing { get; private set; }  // which moment (a key the page chose)
    public event Action? Changed;

    public MomentPlayer() => timer.Tick += (_, _) => Stop();

    public void Toggle(string key, string folder, double from, double length)
    {
        if (Playing == key) { Stop(); return; }
        Stop();
        length = Math.Max(5, length);
        var start = Math.Max(0, from - 3);
        short[]? mix = null;
        foreach (var name in new[] { "mic.wav", "system.wav" })
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;
            try
            {
                var s = Wav.Read(path, start, length);
                if (mix == null) { mix = s; continue; }
                if (s.Length > mix.Length) (mix, s) = (s, mix);
                for (var i = 0; i < s.Length; i++) mix[i] = (short)Math.Clamp(mix[i] + s[i], short.MinValue, short.MaxValue);
            }
            catch (IOException e) { Paths.WriteLog("playing a moment: " + e.Message); }
        }
        if (mix is not { Length: > 0 }) return;
        var cancel = playing = new CancellationTokenSource();
        Sound.Play(mix, cancel.Token, () => MainLoop.Instance.Post(_ => { if (playing == cancel) Stop(); }, null));
        Playing = key;
        timer.Interval = (int)(length * 1000);
        timer.Start();
        Changed?.Invoke();
    }

    public void Stop()
    {
        timer.Stop();
        playing?.Cancel();
        playing = null;
        if (Playing == null) return;
        Playing = null;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        timer.Dispose();
    }
}

/// Sound out, through the sound server: a moment of a meeting, the "mark" sound.
static unsafe class Sound
{
    /// Plays 16 kHz mono samples on a thread of its own; done is called at the end (not when cancelled).
    public static void Play(short[] samples, CancellationToken cancel, Action? done = null) => new Thread(() =>
    {
        var spec = new Native.PaSampleSpec { Format = Native.PaSampleS16Le, Rate = Wav.Rate, Channels = 1 };
        var attr = new Native.PaBufferAttr { MaxLength = uint.MaxValue, TLength = Wav.Rate / 5 * 2, PreBuf = uint.MaxValue, MinReq = uint.MaxValue, FragSize = uint.MaxValue };
        IntPtr s;
        try { s = Native.pa_simple_new(null, "Hesper", Native.PaStreamPlayback, null, "Playback", ref spec, IntPtr.Zero, ref attr, out var error); }
        catch (DllNotFoundException) { return; }
        if (s == IntPtr.Zero) { Paths.WriteLog("can't play sound"); return; }
        try
        {
            const int chunk = Wav.Rate / 10;
            for (var i = 0; i < samples.Length && !cancel.IsCancellationRequested; i += chunk)
            {
                var n = Math.Min(chunk, samples.Length - i);
                fixed (short* p = &samples[i])
                    if (Native.pa_simple_write(s, (byte*)p, (nuint)(n * 2), out _) < 0) break;
            }
            if (cancel.IsCancellationRequested) Native.pa_simple_flush(s, out _);
            else { Native.pa_simple_drain(s, out _); done?.Invoke(); }
        }
        finally { Native.pa_simple_free(s); }
    }) { IsBackground = true, Name = "sound" }.Start();

    static short[]? ding;

    /// A moment marked: two short soft notes, rising.
    public static void Ding()
    {
        ding ??= Tone([(988, 0.07), (1319, 0.12)]);
        Play(ding, CancellationToken.None);
    }

    static short[] Tone((double Hz, double Seconds)[] notes)
    {
        var all = new List<short>();
        foreach (var (hz, seconds) in notes)
        {
            var n = (int)(seconds * Wav.Rate);
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Wav.Rate;
                var envelope = Math.Min(1, i / (0.004 * Wav.Rate)) * Math.Exp(-t * 18);
                all.Add((short)(Math.Sin(2 * Math.PI * hz * t) * envelope * 0.35 * short.MaxValue));
            }
        }
        return [.. all];
    }
}
