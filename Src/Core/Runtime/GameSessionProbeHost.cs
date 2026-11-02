using Atria.Core.Contracts.Window;
using Atria.Core.Runtime.Clock;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation.UI;

namespace Atria.Core.Runtime;

/// <summary>Platform resources supplied by Windows-only integration probes.</summary>
internal sealed class GameSessionProbeHost(GameSession session, IGameWindow window, ITextRasterizer rasterizer, IDisposable lifetime) : IDisposable
{
    public GameSession Session { get; } = session;
    public IGameWindow Window { get; } = window;
    /// <summary>Releases the text backend and all native resources owned by the probe.</summary>
    public void Dispose()
    {
        try { UnicodeText.ReleaseRasterizer(rasterizer); }
        finally { lifetime.Dispose(); }
    }
}

/// <summary>Creates one native host for an existing session integration scenario.</summary>
internal delegate GameSessionProbeHost GameSessionProbeHostFactory(string root, string musicRoot, string output, double maximumSeconds, bool muted);
