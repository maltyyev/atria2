using Atria.Platform.Input;
using Atria.Video;

namespace Atria.Game;

public delegate MoviePlaybackResult PlayGameMovie(string path, Func<bool> skipRequested, float volume, double maximumSeconds);

/// <summary>Host-owned services that differ between Windows and mobile sessions.</summary>
public sealed record GameHostServices(string InterfaceRoot, IGamepadDevice Gamepads, PlayGameMovie PlayMovie);
