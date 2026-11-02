using Atria.Core.Game.Progression;
namespace Atria.Core.Contracts.Presentation;

/// <summary>
/// Transfers complete 32-bit BGRA frames from the game thread to a platform presenter.
/// <see cref="SetFrameSize"/> establishes the dimensions required by the next frame;
/// <see cref="Present"/> consumes the span synchronously, so callers may reuse its memory on return.
/// Implementations may pace the producer, but must not advance game simulation or retain the span.
/// </summary>
public interface IFramePresenter
{
    /// <summary>Updates the display surface size without changing the logical frame dimensions.</summary>
    void Resize(int width, int height);

    /// <summary>Sets the dimensions of each subsequent tightly packed BGRA frame.</summary>
    void SetFrameSize(int width, int height);

    /// <summary>Consumes a tightly packed BGRA frame before returning; may wait to pace presentation.</summary>
    void Present(ReadOnlySpan<uint> pixels);
}
