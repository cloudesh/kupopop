using System.Runtime.InteropServices;

namespace Kupopop;

/// <summary>Independent native waveform handles leave other plugins' audio unaffected.</summary>
internal sealed class GameAudio(DiagnosticLog log) : IDisposable
{
    private readonly Dictionary<string, Clip> clips = [];
    private readonly Queue<string> pending = [];
    private Clip? active;
    private bool unavailable;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();

    public void Enqueue(string cue)
    {
        if (!unavailable && pending.Count < 4) pending.Enqueue(cue);
    }

    public void Tick()
    {
        if (unavailable) return;
        try
        {
            if (active is not null && (Marshal.PtrToStructure<WaveHeader>(active.Header).Flags & 1) == 0) return;
            active = null;
            if (!pending.TryDequeue(out string? cue)) return;
            if (!clips.TryGetValue(cue, out var clip)) clips.Add(cue, clip = Load(cue));
            Check(waveOutWrite(clip.Device, clip.Header, HeaderSize));
            active = clip;
        }
        catch (Exception ex)
        {
            unavailable = true;
            pending.Clear();
            log.Write("audio.unavailable", ex);
        }
    }

    private static Clip Load(string cue)
    {
        using var stream = typeof(GameAudio).Assembly.GetManifestResourceStream($"Kupopop.Audio.{cue}.wav")
            ?? throw new InvalidDataException("Bundled game sound missing.");
        if (stream.Length is < 44 or > 1_000_000) throw new InvalidDataException("Invalid game sound size.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        // Assets are canonical PCM WAVs; each clip owns its format/device.
        if (!bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(36, 4).SequenceEqual("data"u8) ||
            BitConverter.ToUInt16(bytes, 20) != 1 || BitConverter.ToUInt32(bytes, 40) != bytes.Length - 44)
            throw new InvalidDataException("Invalid game sound format.");
        var format = new WaveFormat
        {
            Tag = 1, Channels = BitConverter.ToUInt16(bytes, 22), SamplesPerSecond = BitConverter.ToUInt32(bytes, 24),
            BytesPerSecond = BitConverter.ToUInt32(bytes, 28), BlockAlign = BitConverter.ToUInt16(bytes, 32),
            BitsPerSample = BitConverter.ToUInt16(bytes, 34),
        };
        Check(waveOutOpen(out nint device, uint.MaxValue, ref format, 0, 0, 0));
        nint data = 0, header = 0;
        try
        {
            data = Marshal.AllocHGlobal(bytes.Length - 44);
            Marshal.Copy(bytes, 44, data, bytes.Length - 44);
            header = Marshal.AllocHGlobal((int)HeaderSize);
            Marshal.StructureToPtr(new WaveHeader { Data = data, Length = (uint)(bytes.Length - 44) }, header, false);
            Check(waveOutPrepareHeader(device, header, HeaderSize));
            return new(device, data, header);
        }
        catch
        {
            if (header != 0) Marshal.FreeHGlobal(header);
            if (data != 0) Marshal.FreeHGlobal(data);
            waveOutClose(device);
            throw;
        }
    }

    public void Stop()
    {
        pending.Clear();
        if (active is not null)
        {
            uint result = waveOutReset(active.Device);
            if (result != 0) { unavailable = true; log.Write("audio.stop_failed"); }
            else active = null;
        }
    }

    public void Dispose()
    {
        Stop();
        foreach (var clip in clips.Values)
        {
            // Reset returns buffers before unprepare/free. Never free native-owned memory on failure.
            if (waveOutReset(clip.Device) != 0 || waveOutUnprepareHeader(clip.Device, clip.Header, HeaderSize) != 0)
            { log.Write("audio.cleanup_failed"); continue; }
            waveOutClose(clip.Device);
            Marshal.FreeHGlobal(clip.Header);
            Marshal.FreeHGlobal(clip.Data);
        }
        clips.Clear(); active = null; unavailable = true;
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new InvalidOperationException($"Native waveform audio failed ({result}).");
    }

    private sealed record Clip(nint Device, nint Data, nint Header);
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint SamplesPerSecond, BytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint Length, Recorded;
        public nuint User;
        public uint Flags, Loops;
        public nint Next;
        public nuint Reserved;
    }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint device, uint id, ref WaveFormat format, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(nint device);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(nint device);
}
