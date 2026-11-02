using Atria.Platform.Input;
using Atria.Render.UI;
using Atria.Video;

namespace Atria.Game;

public delegate MoviePlaybackResult PlayGameMovie(string path, Func<bool> skipRequested, float volume, double maximumSeconds);

/// <summary>Platform services selected by the host; the session owns the supplied text backend's lifetime.</summary>
public sealed record GameHostServices(string InterfaceRoot, IGamepadDevice Gamepads, PlayGameMovie PlayMovie, ITextRasterizer TextRasterizer);
