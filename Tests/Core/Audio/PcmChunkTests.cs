using Atria.Core.Contracts.Audio;
using Atria.Core.Resources;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;
using Tests.Core.Persistence;

namespace Tests.Core.Audio;

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
