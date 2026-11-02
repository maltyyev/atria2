namespace Atria.Core.Contracts.Video;

/// <summary>Playback outcome shared by the game loop and each host's movie backend.</summary>
public readonly record struct MoviePlaybackResult(int FramesAdvanced, int Presents, long AudioSampleFrames, bool Skipped, bool TimedOut);
