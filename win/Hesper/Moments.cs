using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Hesper;

/// Plays a moment of a meeting (a marked one, or where a to-do was said): both tracks together,
/// your mic and the meeting, from a little before it. One at a time. As the Mac's MomentPlayer.
sealed class MomentPlayer : IDisposable
{
    WasapiOut? output;
    readonly List<WaveFileReader> readers = [];
    readonly System.Windows.Forms.Timer timer = new();
    public string? Playing { get; private set; }  // which moment (a key the page chose)
    public event Action? Changed;

    public MomentPlayer() => timer.Tick += (_, _) => Stop();

    public void Toggle(string key, string folder, double from, double length)
    {
        if (Playing == key) { Stop(); return; }
        Stop();
        var start = Math.Max(0, from - 3);
        foreach (var name in new[] { "mic.wav", "system.wav" })
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;
            var r = new WaveFileReader(path);
            if (start >= r.TotalTime.TotalSeconds) { r.Dispose(); continue; }
            r.CurrentTime = TimeSpan.FromSeconds(start);
            readers.Add(r);
        }
        if (readers.Count == 0) return;
        try
        {
            ISampleProvider mix = new MixingSampleProvider(readers.Select(r => r.ToSampleProvider()));
            using (var devices = new MMDeviceEnumerator())
            {
                var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                var format = device.AudioClient.MixFormat;
                if (mix.WaveFormat.SampleRate != format.SampleRate) mix = new WdlResamplingSampleProvider(mix, format.SampleRate);
                if (format.Channels >= 2) mix = new MonoToStereoSampleProvider(mix);
                output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            }
            output.Init(mix);
            output.PlaybackStopped += (_, _) => { if (output != null) Stop(); };
            output.Play();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            Paths.WriteLog("playing a moment: " + e.Message);
            Stop();
            return;
        }
        Playing = key;
        timer.Interval = (int)(Math.Max(5, length) * 1000);
        timer.Start();
        Changed?.Invoke();
    }

    public void Stop()
    {
        timer.Stop();
        var o = output;
        output = null;
        o?.Stop();
        o?.Dispose();
        readers.ForEach(r => r.Dispose());
        readers.Clear();
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
