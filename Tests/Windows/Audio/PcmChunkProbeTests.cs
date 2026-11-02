using Atria.Core.Resources.Audio;
using Atria.Platforms.Windows.Audio;

namespace Tests.Windows.Audio;

/// <summary>Checks the native movie PCM stream's pause, resume, and sample-count behavior.</summary>
public static class PcmChunkProbeTests
{
    public static string RunMutedProbe()
    {
        string path = Path.GetTempFileName();
        try
        {
            // One second of silent stereo16 PCM interleaved with bytes that are not audio.
            byte[] source = new byte[176432];
            source.AsSpan(88200, 32).Fill(0x7f);
            File.WriteAllBytes(path, source);
            using var device = new AudioDevice();
            using var stream = device.PlayPcmChunks(path, [new(0, 88200), new(88232, 88200)], new(2, 44100, 16), 0, true);
            Thread.Sleep(60);
            if (stream.SamplesPlayed != 0 || stream.Finished || stream.BufferCapacityBytes != 196608)
                throw new InvalidOperationException("Paused movie PCM prefill advanced its sample clock.");

            stream.Start();
            Thread.Sleep(120);
            ThrowFailure(stream);
            if (stream.SamplesPlayed <= 0)
                throw new InvalidOperationException("Movie PCM failed to start.");

            stream.Pause();
            Thread.Sleep(60);
            long paused = stream.SamplesPlayed;
            Thread.Sleep(60);
            if (stream.SamplesPlayed != paused)
                throw new InvalidOperationException("Paused movie PCM clock advanced.");

            stream.Resume();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!stream.Finished && timer.ElapsedMilliseconds < 3000)
                Thread.Sleep(5);

            ThrowFailure(stream);
            if (!stream.Finished || stream.SamplesPlayed != 44100)
                throw new InvalidOperationException($"Continuous fragmented PCM clock ended at {stream.SamplesPlayed} instead of 44100 frames.");

            return "Native fragmented PCM: paused prefill, Start/Pause/Resume, bounded 196608-byte buffers and exact 44100-frame completion passed.";
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void ThrowFailure(PcmChunkStream stream)
    {
        if (stream.Failure is { } failure)
            throw new InvalidOperationException("PCM worker failed.", failure);
    }
}
