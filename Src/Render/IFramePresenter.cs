namespace Atria.Render;

public interface IFramePresenter
{
    void Resize(int width, int height);
    void SetFrameSize(int width, int height);
    void Present(ReadOnlySpan<uint> pixels);
}
