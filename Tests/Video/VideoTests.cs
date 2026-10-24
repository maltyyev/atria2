using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Atria.Audio;
using Atria.Render;
using Atria.Video;

namespace Atria.Tests.Video;

public static class VideoTests
{
    public static string Run(string root)
    {
        if (Viewport.Fit(2560, 1440, 380, 240) != new Viewport(140, 0, 2280, 1440))
        {
            throw new Exception("Movie aspect failed.");
        }

        for (int frame = 1; frame <= 1443; frame++)
        {
            if (MoviePlayer.FrameNumber(frame * 1470 - 1, 22050, 0, 15, 1) != frame - 1 || MoviePlayer.FrameNumber(frame * 1470, 22050, 0, 15, 1) != frame)
            {
                throw new Exception("Exact audio/video frame boundary failed.");
            }
        }

        if (MoviePlayer.FrameNumber(0, 22050, TimeSpan.TicksPerSecond, 15, 1) != 15)
        {
            throw new Exception("Movie silent tail timeline failed.");
        }

        int decoded = 0, held = 0;
        foreach (string name in new[]
        {
            "logo",
            "medal",
            "jamie1"
        }
        )
        {
            using var movie = new AviMovie(Path.Combine(root, name + ".jad"));
            using var decoder = new Indeo3Decoder(movie.Width, movie.Height);
            byte[] packet = new byte[movie.MaximumPacketBytes];
            uint[] pixels = new uint[movie.Width * movie.Height];
            var fixture = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "VideoFixtures", name + ".framemd5")).Where(line => !line.StartsWith('#')).Select(line => line.Split(',').Select(v => v.Trim()).ToArray()).ToDictionary(row => int.Parse(row[2], CultureInfo.InvariantCulture), row => row[5]);
            int actual = 0;
            for (int i = 0; i < movie.Frames.Count; i++)
            {
                int length = movie.ReadFrame(i, packet);
                if (length == 0)
                {
                    held++;
                    continue;
                }

                decoder.Decode(packet.AsSpan(0, length), pixels);
                string hash = Convert.ToHexString(MD5.HashData(MemoryMarshal.AsBytes(pixels.AsSpan()))).ToLowerInvariant();
                if (!fixture.TryGetValue(i, out string? expected) || hash != expected)
                {
                    throw new Exception($"Movie {name} frame {i} differs from FFmpeg demux/decode fixture ({hash} vs {expected}).");
                }

                actual++;
                decoded++;
            }

            if (actual != fixture.Count)
            {
                throw new Exception("Movie fixture frame count mismatch.");
            }

            using var pcm = new PcmChunkReader(movie.Path, movie.AudioChunks, movie.AudioFormat);
            using var audioHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536];
            long read = 0;
            while (pcm.RemainingBytes > 0)
            {
                int count = pcm.Read(buffer);
                read += count;
                audioHash.AppendData(buffer, 0, count);
            }

            if (read != movie.AudioByteCount)
            {
                throw new Exception("Movie PCM length mismatch.");
            }

            string expectedAudio = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "VideoFixtures", name + ".audio.sha256")).Trim()[7..];
            if (!Convert.ToHexString(audioHash.GetHashAndReset()).Equals(expectedAudio, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Movie PCM differs from FFmpeg stream-copy fixture.");
            }
        }

        return $"Video: three original AVI files, {decoded} decoded frame hashes match FFmpeg CLI, {held} empty frames retain timeline; PCM ranges and undistorted movie viewport passed. Original decoder pixel parity remains unverified.";
    }
}
