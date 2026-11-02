using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Contracts.Video;

namespace Atria.Core.Contracts.Host;

public delegate MoviePlaybackResult PlayGameMovie(string path, Func<bool> skipRequested, float volume, double maximumSeconds);

/// <summary>Platform services selected by the host; the session owns the supplied text backend's lifetime.</summary>
public sealed record GameHostServices(string InterfaceRoot, IGamepadDevice Gamepads, PlayGameMovie PlayMovie, ITextRasterizer TextRasterizer);
