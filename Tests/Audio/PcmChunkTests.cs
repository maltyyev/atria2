using Atria.Audio;

namespace Atria.Tests.Audio;

public static class PcmChunkTests
{
    public static string Run()
    {
        static void Check(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidOperationException(message);
            }
        }

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
            var chunks = new[]
            {
                new PcmChunk(4, 8),
                new PcmChunk(20, 0),
                new PcmChunk(20, 4),
                new PcmChunk(0, 4)
            };
            var format = new PcmAudioFormat(2, 44100, 16);
            using (var reader = new PcmChunkReader(path, chunks, format))
            {
                Check(reader.TotalBytes == 16 && format.BlockAlign == 4 && format.ByteRate == 176400, "PCM byte/frame arithmetic.");
                chunks[0] = new(100000, 1); // Reader owns validated range metadata.
                byte[] first = new byte[8], second = new byte[8];
                Check(reader.Read(first) == 8 && first.AsSpan().SequenceEqual(new byte[] { 4, 5, 6, 7, 8, 9, 10, 11 }), "First fragmented PCM range.");
                Check(reader.Read(second) == 8 && second.AsSpan().SequenceEqual(new byte[] { 20, 21, 22, 23, 0, 1, 2, 3 }), "Chunk aggregation must exclude unrelated AVI bytes.");
                Check(reader.RemainingBytes == 0 && reader.Read(first) == 0, "Fragmented PCM EOF.");
            }

            Reject(() => new PcmChunkReader(path, [new(-1, 4)], format));
            Reject(() => new PcmChunkReader(path, [new(30, 4)], format));
            Reject(() => new PcmChunkReader(path, [new(long.MaxValue, 4)], format));
            Reject(() => new PcmChunkReader(path, [new(0, -4)], format));
            Reject(() => new PcmChunkReader(path, [new(0, 3)], format));
            Reject(() => new PcmChunkReader(path, [], new(0, 44100, 16)));
            Reject(() => new PcmChunkReader(path, [], new(2, 0, 16)));
            using (var empty = new PcmChunkReader(path, [], format))
            {
                Check(empty.TotalBytes == 0 && empty.Read(new byte[4]) == 0, "Empty audio range reader.");
            }

            return "Fragmented PCM: exact byte concatenation, metadata ownership, empty ranges, alignment and hostile file-bound checks passed.";
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static string RunMutedProbe()
    {
        string path = Path.GetTempFileName();
        try
        {
            // One second of silent stereo16 PCM interleaved with bytes that are NOT audio.
            byte[] source = new byte[176432];
            source.AsSpan(88200, 32).Fill(0x7f);
            File.WriteAllBytes(path, source);
            using var device = new AudioDevice();
            using var stream = device.PlayPcmChunks(path, [new(0, 88200), new(88232, 88200)], new(2, 44100, 16), 0, true);
            Thread.Sleep(60);
            if (stream.SamplesPlayed != 0 || stream.Finished || stream.BufferCapacityBytes != 196608)
            {
                throw new InvalidOperationException("Paused movie PCM prefill advanced its sample clock.");
            }

            stream.Start();
            Thread.Sleep(120);
            ThrowFailure(stream);
            if (stream.SamplesPlayed <= 0)
            {
                throw new InvalidOperationException("Movie PCM failed to start.");
            }

            stream.Pause();
            Thread.Sleep(60);
            long paused = stream.SamplesPlayed;
            Thread.Sleep(60);
            if (stream.SamplesPlayed != paused)
            {
                throw new InvalidOperationException("Paused movie PCM clock advanced.");
            }

            stream.Resume();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!stream.Finished && timer.ElapsedMilliseconds < 3000)
            {
                Thread.Sleep(5);
            }

            ThrowFailure(stream);
            if (!stream.Finished || stream.SamplesPlayed != 44100)
            {
                throw new InvalidOperationException($"Continuous fragmented PCM clock ended at {stream.SamplesPlayed} instead of44100 frames.");
            }

            return "Native fragmented PCM: paused prefill, Start/Pause/Resume, bounded196608-byte buffers and exact44100-frame completion passed.";
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void ThrowFailure(PcmChunkStream stream)
    {
        if (stream.Failure is { } failure)
        {
            throw new InvalidOperationException("PCM worker failed.", failure);
        }
    }

    private static void Reject(Func<IDisposable> action)
    {
        try
        {
            using var value = action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid PCM metadata accepted.");
    }
}
