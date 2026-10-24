using System.Diagnostics;
using Atria.Platform;
using Atria.Platform.Win32;
using Atria.Render;
using Atria.Resources;

namespace Atria;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || (args.Length == 1 && args[0] == "--playtest"))
            {
                return RunPlaytest();
            }

            if (args.Length >= 3 && args[0] == "--session-probe")
            {
                return Atria.Game.GameSession.Run(
                    Path.GetFullPath(args[1]),
                    Path.GetFullPath(args[2]),
                    args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : double.PositiveInfinity,
                    true,
                    args.Length > 4 ? Path.GetFullPath(args[4]) : null
                );
            }

            if (args.Length >= 3 && args[0] == "--movie-probe")
            {
                var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(Path.GetFullPath(args[1]), "BATTLE.CFG")));
                using var movieWindow = new NativeWindow("Atria II — native movie verification", 2560, 1440);
                using var movieRenderer = new D3D11Presenter(movieWindow.Handle, 2560, 1440);
                using var audio = new Atria.Audio.AudioDevice();
                var result = Atria.Video.MoviePlayer.Play(
                    Path.Combine(Path.GetFullPath(args[1]), args[2]),
                    movieWindow,
                    movieRenderer,
                    audio,
                    () => movieWindow.ScanPressed[1] || movieWindow.ScanPressed[bindings.Confirm] || movieWindow.ScanPressed[bindings.Cancel],
                    0,
                    args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : double.PositiveInfinity
                );
                Console.WriteLine($"Native movie: {result}; dynamicCode={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}.");
                return result.FramesAdvanced > 0 ? 0 : 3;
            }

            if (args.Length >= 2 && args[0] == "--effects-probe")
            {
                using var audio = new Atria.Audio.AudioDevice();
                using var waves = BinderArchive.Open(Path.Combine(Path.GetFullPath(args[1]), "AT2WAV.000"));
                var effect = audio.LoadEffect(waves.ReadMember(waves.FindResource("MAINMENU"u8)));
                for (int i = 0; i < 9; i++)
                {
                    effect.SetCurrentVolume(-10000);
                    effect.Play();
                }

                uint queued = effect.QueuedVoiceCount;
                effect.StopAll();
                for (int i = 0; i < 20 && effect.QueuedVoiceCount != 0; i++)
                {
                    Thread.Sleep(10);
                }

                if (queued != 8 || effect.Cursor != 1 || effect.QueuedVoiceCount != 0)
                {
                    return 3;
                }

                effect.Play();
                var finish = Stopwatch.StartNew();
                while (effect.QueuedVoiceCount != 0 && finish.Elapsed.TotalSeconds < effect.Format.DurationSeconds + 2)
                {
                    Thread.Sleep(5);
                }

                Console.WriteLine($"Effect voices: queuedAfter9={queued}, cursorAfterStopAndReplay={effect.Cursor}, queuedAtEnd={effect.QueuedVoiceCount}, sampleDuration={effect.Format.DurationSeconds:F3}s.");
                return effect.Cursor == 2 && effect.QueuedVoiceCount == 0 ? 0 : 3;
            }

            if (args.Length >= 3 && args[0] == "--menu-probe")
            {
                return Atria.Game.GameSession.Run(
                    Path.GetFullPath(args[1]),
                    Path.GetFullPath(args[2]),
                    args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 60,
                    true
                );
            }

            if (args.Length >= 2 && args[0] == "--audio-probe")
            {
                using var audio = new Atria.Audio.AudioDevice();
                using var stream = audio.PlayFile(Path.GetFullPath(args[1]), 0);
                Stopwatch watch = Stopwatch.StartNew();
                double seconds = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 2;
                while (!stream.Finished && watch.Elapsed.TotalSeconds < seconds)
                {
                    Thread.Sleep(10);
                }

                if (stream.Failure is not null)
                {
                    throw new InvalidOperationException("Audio streaming failed.", stream.Failure);
                }

                Console.WriteLine($"Audio stream: sampleFrames={stream.SamplesPlayed}, rate={stream.Format.SampleRate}, finished={stream.Finished}, boundedBuffers=196608 bytes.");
                return stream.SamplesPlayed > 0 ? 0 : 3;
            }

            if (args.Length < 3 || args[0] != "--preview")
            {
                Console.WriteLine($"Atria {BuildInfo.Version} development build. Full gameplay is not implemented yet.");
                Console.WriteLine("Asset presentation verification: --preview <original-resource-directory> <BMP-member> [seconds]");
                return 2;
            }

            string root = Path.GetFullPath(args[1]);
            using BinderArchive archive = BinderArchive.Open(Path.Combine(root, "AT2BMP.000"));
            int index = archive.FindResource(System.Text.Encoding.ASCII.GetBytes(args[2]));
            if (index < 0)
            {
                throw new FileNotFoundException("BMP member not found.", args[2]);
            }

            BmpImage image = BmpImage.Decode(archive.ReadMember(index));
            if (image.Width != 640 || image.Height != 480)
            {
                throw new InvalidDataException("Preview expects an original640x480 bitmap.");
            }

            using NativeWindow window = new("Atria II — renderer verification", 2560, 1440);
            using D3D11Presenter renderer = new(window.Handle, 2560, 1440);
            double duration = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : double.PositiveInfinity;
            Stopwatch timer = Stopwatch.StartNew();
            int frames = 0;
            while (!window.Closed && timer.Elapsed.TotalSeconds < duration)
            {
                window.Pump();
                if (window.Pressed[27])
                {
                    break;
                }

                (int width, int height) = window.ClientSize;
                if (width <= 0 || height <= 0 || !window.Focused)
                {
                    Thread.Sleep(20);
                    continue;
                }

                renderer.Resize(width, height);
                renderer.Present(image.Pixels);
                frames++;
            }

            Console.WriteLine($"Renderer verified: frames={frames}, elapsed={timer.Elapsed.TotalSeconds:F3}, viewport={renderer.ContentViewport}, feature=0x{renderer.FeatureLevel:X}.");
            return frames > 0 ? 0 : 3;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private static int RunPlaytest()
    {
        string output = Path.Combine(AppContext.BaseDirectory, "playtest-data");
        string logs = Path.Combine(output, "logs");
        string logPath = Path.Combine(logs, $"atria-{BuildInfo.Version}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
        TextWriter previousOut = Console.Out, previousError = Console.Error;
        try
        {
            Directory.CreateDirectory(logs);
            using var log = new StreamWriter(logPath, false, System.Text.Encoding.UTF8)
            {
                AutoFlush = true
            };
            Console.SetOut(log);
            Console.SetError(log);
            try
            {
                string resources = Path.Combine(AppContext.BaseDirectory, "assets", "game");
                string music = Path.Combine(AppContext.BaseDirectory, "assets", "music");
                Console.WriteLine($"Atria {BuildInfo.Version}; {DateTimeOffset.Now:O}; dynamicCode={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");
                Console.WriteLine($"Resources: {resources}\nMusic: {music}\nUser data: {output}");
                Console.WriteLine("Development playtest: combat timing and full-game fidelity are not yet validated.");
                return Atria.Game.GameSession.Run(resources, music, double.PositiveInfinity, false, outputDirectory: output);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                throw;
            }
            finally
            {
                Console.SetOut(previousOut);
                Console.SetError(previousError);
            }
        }
        catch (Exception e)
        {
            ShowLaunchError(0, $"Atria {BuildInfo.Version}\n{e.Message}\n\nЖурнал: {logPath}", "Atria II", 0x10);
            return 1;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern int ShowLaunchError(nint window, string text, string caption, uint type);
}
