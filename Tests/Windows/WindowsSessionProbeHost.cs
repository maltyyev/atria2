using Atria.Core.Contracts.Audio;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;
using Atria.Core.Contracts.Host;
using Atria.Core.Game;
using Atria.Core.Runtime;
using Atria.Core.Game.Dialogue;
using Atria.Core.Presentation.Dialogue;
using Atria.Core.Resources.Sprites;
using Atria.Core.Runtime.Input;
using Atria.Core.Runtime.Timing;
using Atria.Platforms.Windows.Win32;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation;
using Atria.Core.Presentation.UI;
using System.Runtime.ExceptionServices;
using Atria.Core.Persistence;
using Atria.Platforms.Windows.Audio;
using Atria.Platforms.Windows.Render;
using Atria.Platforms.Windows.Video;

namespace Tests.Windows;

/// <summary>Builds the real Windows devices for existing private-state session probes.</summary>
internal static class WindowsSessionProbeHost
{
    /// <summary>Creates the native devices and a shared session for one existing verification scenario.</summary>
    public static GameSessionProbeHost Create(string root, string musicRoot, string output, double maximumSeconds, bool muted)
    {
        var owned = new List<IDisposable>();
        ITextRasterizer? rasterizer = null;
        try
        {
            var window = new NativeWindow("Atria session verification", 2560, 1440);
            owned.Add(window);
            var presenter = new D3D11Presenter(window.Handle, 2560, 1440);
            owned.Add(presenter);
            var audio = new AudioDevice();
            owned.Add(audio);
            var music = new FileMusicTransport(audio, musicRoot, muted ? 0 : 1);
            owned.Add(music);
            var faces = new FaceSpriteSource(Path.Combine(root, "face.SPR"));
            owned.Add(faces);
            rasterizer = new DirectWriteRasterizer();
            var services = new GameHostServices(Path.Combine(AppContext.BaseDirectory, "assets"), new XInputDevice(),
                (path, skip, volume, duration) => MoviePlayer.Play(path, window, presenter, audio, skip, volume, duration), rasterizer);
            var session = GameSession.CreateProbeSession(root, window, presenter, audio, music, faces, maximumSeconds, muted, output, services);
            return new(session, window, rasterizer, new Lifetime(owned.ToArray()));
        }
        catch
        {
            if (rasterizer is not null) UnicodeText.ReleaseRasterizer(rasterizer);
            try { new Lifetime(owned.ToArray()).Dispose(); }
            catch { }
            throw;
        }
    }

    /// <summary>Checks reverse-order disposal and cleanup after one owned resource throws.</summary>
    public static void VerifyLifetimeContract()
    {
        var order = new List<int>();
        var lifetime = new Lifetime([new FakeDisposable(1, order), new FakeDisposable(2, order, true), new FakeDisposable(3, order)]);
        bool failed = false;
        try { lifetime.Dispose(); }
        catch (InvalidOperationException) { failed = true; }
        if (!failed || !order.SequenceEqual([3, 2, 1]))
            throw new InvalidOperationException("Probe resource ownership did not dispose in reverse order after failure.");
    }

    /// <summary>Owns host resources in construction order and disposes every one in reverse order.</summary>
    private sealed class Lifetime(IDisposable[] owned) : IDisposable
    {
        private bool disposed;

        /// <summary>Disposes all resources, then rethrows the first disposal failure.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Exception? failure = null;
            for (int i = owned.Length - 1; i >= 0; i--)
            {
                try { owned[i].Dispose(); }
                catch (Exception error) { failure ??= error; }
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Records a disposal attempt and can inject a failure for the ownership check.</summary>
    private sealed class FakeDisposable(int id, List<int> order, bool fail = false) : IDisposable
    {
        public void Dispose()
        {
            order.Add(id);
            if (fail) throw new InvalidOperationException("expected disposal failure");
        }
    }
}
