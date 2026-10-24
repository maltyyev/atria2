using System.Diagnostics;
using Atria.Audio;
using Atria.Platform.Win32;

namespace Atria.Tests.Audio;

/// <summary>Real muted XAudio2 lifecycle checks; these do not establish audible PCM parity.</summary>
public static class MusicStreamTests
{
    public static string Run(string musicRoot)
    {
        CheckBufferOwnership();
        string directory = Path.Combine(Path.GetTempPath(), "atria-music-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var device = new AudioDevice();
            // Exercise every supplied file through the actual streaming voice, not only its RIFF parser.
            for (int track = 2; track <= 28; track++)
            {
                string path = Path.Combine(musicRoot, $"Track{track:00}.wav");
                using var stream = device.PlayFile(path, 0);
                Await(stream, () => stream.SamplesPlayed > 0, $"Track{track:00} sample clock did not start.");
                if (stream.Format.Channels != 2 || stream.Format.SampleRate != 44100 || stream.Format.BitsPerSample != 16)
                {
                    throw new InvalidOperationException("Supplied soundtrack format changed.");
                }

                stream.SetVolume(0);
            }

            using (var transport = new FileMusicTransport(device, musicRoot, 0))
            {
                var controller = new MusicController(transport);
                controller.Update(1);
                if (transport.CapturePlayingTrack() != 1)
                {
                    throw new InvalidOperationException("Logical music1 did not start.");
                }

                controller.Update(2);
                if (transport.CapturePlayingTrack() != 1)
                {
                    throw new InvalidOperationException("Skipped music poll changed the real stream.");
                }

                controller.Update(2);
                if (transport.CapturePlayingTrack() != 2)
                {
                    throw new InvalidOperationException("Track replacement failed.");
                }

                controller.Update(0);
                controller.Update(0);
                if (transport.State != MusicPlaybackState.Stopped || transport.CapturePlayingTrack() != 0 || controller.CachedTrack != 2)
                {
                    throw new InvalidOperationException("Explicit stop lost the retained music cache or left a voice active.");
                }

                controller.Update(2);
                controller.Update(2);
                if (transport.CapturePlayingTrack() != 2)
                {
                    throw new InvalidOperationException("Stopped cached track did not restart.");
                }
            }

            string extraTrack = Path.Combine(directory, "Track41.wav");
            WriteSilence(extraTrack, 44100);
            using (var transport = new FileMusicTransport(device, directory, 0))
            {
                transport.ValidateTrack(40);
                transport.Play(40);
                if (transport.CapturePlayingTrack() != 40)
                {
                    throw new Exception("Discovered track above the old fixed limit did not start.");
                }

                try
                {
                    transport.Play(2);
                    throw new Exception("Missing track was accepted.");
                }
                catch (FileNotFoundException)
                {
                }

                if (transport.CapturePlayingTrack() != 40)
                {
                    throw new Exception("Missing track stopped an existing transport stream.");
                }
            }

            AssertReleased(extraTrack);

            // Empty, shorter-than-one-buffer, exact boundary and four-buffer refill/tail cases.
            foreach (int frames in new[]
            {
                0,
                1,
                16384,
                3 * 16384 + 17
            }
            )
            {
                string path = Path.Combine(directory, $"frames-{frames}.wav");
                WriteSilence(path, frames);
                var stream = device.PlayFile(path, 0);
                try
                {
                    Await(stream, () => stream.Finished, "Music stream did not drain.");
                    if (stream.SamplesPlayed != frames)
                    {
                        throw new InvalidOperationException($"Music ended at{stream.SamplesPlayed}, expected{frames} frames.");
                    }

                    long ended = stream.SamplesPlayed;
                    Thread.Sleep(30);
                    if (!stream.Finished || stream.SamplesPlayed != ended)
                    {
                        throw new InvalidOperationException("Finished stream restarted without a controller request.");
                    }
                }
                finally
                {
                    stream.Dispose();
                    stream.Dispose();
                }

                AssertReleased(path);
            }

            string owned = Path.Combine(directory, "owner-dispose.wav");
            WriteSilence(owned, 44100 * 2);
            var active = device.PlayFile(owned, 0);
            Await(active, () => active.SamplesPlayed > 0, "Owned stream did not start.");
            device.Dispose(); // Device shutdown must join the feeder before freeing its buffers/voice.
            active.Dispose();
            AssertReleased(owned);
            string invalid = Path.Combine(directory, "invalid.wav");
            File.WriteAllBytes(invalid, new byte[44]);
            using (var nextDevice = new AudioDevice())
            {
                try
                {
                    using var rejected = nextDevice.PlayFile(invalid, 0);
                    throw new InvalidOperationException("Invalid RIFF was played.");
                }
                catch (InvalidDataException)
                {
                }

                AssertReleased(invalid);
                // A failed constructor must not poison later voice creation.
                using var recovered = nextDevice.PlayFile(owned, 0);
                Await(recovered, () => recovered.SamplesPlayed > 0, "Voice creation failed after invalid RIFF.");
            }

            return "Native music streaming: all27 supplied tracks start muted; empty/short/exact/refill tails drain with exact sample counts; stop/owner disposal and malformed-file recovery release handles. Audibility and output PCM parity remain separate.";
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                File.Delete(file);
            }

            Directory.Delete(directory);
        }
    }

    private static void CheckBufferOwnership()
    {
        using var buffer = new Atria.Platform.Win32.NativePcmBuffer(8);
        buffer.Bytes.Fill(42);
        if (buffer.Bytes.Length != 8 || buffer.Bytes[7] != 42 || buffer.Pointer == 0)
        {
            throw new Exception("PCM buffer did not retain its owned sample bytes.");
        }

        buffer.Dispose();
        buffer.Dispose();
        try
        {
            _ = buffer.Bytes;
            throw new Exception("Disposed PCM storage remained accessible.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void Await(MusicStream stream, Func<bool> ready, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!ready() && timer.ElapsedMilliseconds < 5000)
        {
            if (stream.Failure is { } failure)
            {
                throw new InvalidOperationException("Music feeder failed.", failure);
            }

            Thread.Sleep(4);
        }

        if (stream.Failure is { } error)
        {
            throw new InvalidOperationException("Music feeder failed.", error);
        }

        if (!ready())
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertReleased(string path)
    {
        // Only generated fixtures are opened exclusively; original music stays read-only/shared.
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void WriteSilence(string path, int frames)
    {
        using var writer = new BinaryWriter(File.Create(path));
        int bytes = checked(frames * 4);
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)2);
        writer.Write(44100);
        writer.Write(176400);
        writer.Write((ushort)4);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(bytes);
        writer.Write(new byte[bytes]);
    }
}
