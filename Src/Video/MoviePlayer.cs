using System.Diagnostics;
using Atria.Audio;
using Atria.Platform;
using Atria.Platform.Win32;
using Atria.Render;

namespace Atria.Video;

public readonly record struct MoviePlaybackResult(int FramesAdvanced, int Presents, long AudioSampleFrames, bool Skipped, bool TimedOut);
/// <summary>Synchronous cutscene on existing devices. The PCM sample clock owns the video timeline.</summary>
public static class MoviePlayer
{
    public static MoviePlaybackResult Play(
        string path,
        NativeWindow window,
        D3D11Presenter renderer,
        AudioDevice audio,
        Func<bool> skipRequested,
        float volume = 1,
        double maximumSeconds = double.PositiveInfinity
    )
    {
        using var movie = new AviMovie(path);
        using var decoder = new Indeo3Decoder(movie.Width, movie.Height);
        using var sound = audio.PlayPcmChunks(path, movie.AudioChunks, movie.AudioFormat, volume, true);
        byte[] packet = new byte[movie.MaximumPacketBytes];
        uint[] pixels = new uint[movie.Width * movie.Height];
        int next = 0, presents = 0;
        bool skipped = false, timedOut = false, started = false, paused = false;
        var duration = Stopwatch.StartNew();
        var silentTail = new Stopwatch();
        try
        {
            renderer.SetFrameSize(movie.Width, movie.Height);
            while (true)
            {
                window.Pump();
                if (window.Closed)
                {
                    throw new OperationCanceledException("Game closed during video playback.");
                }

                if (sound.Failure is { } failure)
                {
                    throw new InvalidOperationException("Movie audio playback failed.", failure);
                }

                if (duration.Elapsed.TotalSeconds >= maximumSeconds)
                {
                    timedOut = true;
                    break;
                }

                var size = window.ClientSize;
                if (!window.Focused || size.Width == 0 || size.Height == 0)
                {
                    if (!paused)
                    {
                        sound.Pause();
                        silentTail.Stop();
                        paused = true;
                    }

                    Thread.Sleep(10);
                    continue;
                }

                if (paused)
                {
                    if (started)
                    {
                        sound.Resume();
                    }

                    if (sound.Finished)
                    {
                        silentTail.Start();
                    }

                    paused = false;
                }

                // Original401260 checks escape, configured accept and configured cancel, in that order.
                if (skipRequested())
                {
                    skipped = true;
                    break;
                }

                if (sound.Finished && !silentTail.IsRunning)
                {
                    silentTail.Start();
                }

                long timelineFrame = FrameNumber(sound.SamplesPlayed, movie.AudioFormat.SampleRate, silentTail.Elapsed.Ticks, movie.FrameRate, movie.FrameScale);
                int target = (int)Math.Min(movie.Frames.Count - 1, timelineFrame);
                while (next <= target)
                {
                    int count = movie.ReadFrame(next, packet);
                    decoder.Decode(packet.AsSpan(0, count), pixels);
                    next++;
                }

                renderer.Resize(size.Width, size.Height);
                renderer.Present(pixels);
                presents++;
                if (!started)
                {
                    sound.Start();
                    started = true;
                }

                if (timelineFrame >= movie.Frames.Count && sound.Finished)
                {
                    break;
                }
            }

            return new(next, presents, sound.SamplesPlayed, skipped, timedOut);
        }
        finally
        {
            renderer.SetFrameSize(640, 480);
        }
    }

    public static long FrameNumber(long sampleFrames, uint sampleRate, long tailTicks, uint frameRate, uint frameScale)
    {
        if (sampleFrames < 0 || tailTicks < 0 || sampleRate == 0 || frameRate == 0 || frameScale == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleFrames));
        }
        // Rational arithmetic preserves exact boundaries such as22050/15=1470 samples per frame.
        UInt128 ticks = (UInt128)(ulong)sampleFrames * TimeSpan.TicksPerSecond + (UInt128)(ulong)tailTicks * sampleRate;
        UInt128 result = ticks * frameRate / ((UInt128)sampleRate * frameScale * TimeSpan.TicksPerSecond);
        return checked((long)result);
    }
}
