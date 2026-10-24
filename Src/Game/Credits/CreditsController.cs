using Atria.Render.UI;

namespace Atria.Game.Credits;

public sealed class CreditsText
{
    public const int RowCount = 400, RowBytes = 80, HeaderBytes = 100;
    private readonly byte[] rows;
    public ScrollingText Flow { get; } = new("credits", 248);
    private CreditsText(byte[] rows) => this.rows = rows;
    public ReadOnlySpan<byte> this[int index] => GameText.Legacy($"credits.{index:D3}", rows.AsMemory(checked(index * RowBytes), RowBytes)).Span;
    public static CreditsText Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes + RowCount * RowBytes)
        {
            throw new InvalidDataException("Truncated credits rows.");
        }

        byte[] rows = bytes.Slice(HeaderBytes, RowCount * RowBytes).ToArray();
        for (int i = 0; i < RowCount; i++)
        {
            if (!rows.AsSpan(i * RowBytes, RowBytes).Contains((byte)0))
            {
                throw new InvalidDataException("Unterminated credits row.");
            }
        }

        return new(rows); // Original ignores supplied file's remaining34566 bytes.
    }
}

public readonly record struct CreditsPicture(int Frame, int X, int Y, bool Half = false);
public sealed record CreditsFrame(CreditsPicture[] Pictures, int TextY);
/// <summary>423c30/421690/423d20/423db0/423ec0/423f80/423ff0.</summary>
public sealed partial class CreditsController
{
    public int Speed { get; private set; } = 1;
    public int SpeedCounter
    {
        get; private set;
    } //473150: BSS initial0, deliberately not reset by entry.
    public int Scroll
    {
        get; private set;
    }
    public int TextY
    {
        get; private set;
    }
    public bool Completed
    {
        get; private set;
    }
    public int Picture
    {
        get; private set;
    }
    public int PictureCounter
    {
        get; private set;
    }

    private bool transition;
    private int animationCounter, animationFrame;
    private int scrollLimit = 9120;

    public void Enter(int? textRows = null)
    {
        scrollLimit = textRows is { } count ? checked((count + 14) * 24) : 9120;
        Speed = 1;
        Scroll = 0;
        TextY = 400;
        Completed = false;
        Picture = PictureCounter = 0;
        transition = false;
        animationCounter = 0;
        animationFrame = 10;
    }

    // Native confirm is a held query, cancel is a lazy edge query. Completion checks precede both.
    public void Input(Func<bool> confirmHeld, Func<bool> cancelEdge, Action close)
    {
        if (Completed)
        {
            close();
            return;
        }

        bool held = confirmHeld();
        SpeedCounter++;
        if (SpeedCounter > (held ? 10 : 4))
        {
            Speed = held ? Math.Min(8, Speed * 2) : Math.Max(1, Speed / 2);
            SpeedCounter = 0;
        }

        if (cancelEdge())
        {
            close();
        }
    }

    public CreditsFrame AdvanceFrame()
    {
        List<CreditsPicture> pictures = [];
        if (Picture < 10)
        {
            if (transition)
            {
                pictures.Add(new(Picture - 1, 120, 120));
                pictures.Add(new(Picture, 120, 120, true));
                transition = false;
            }
            else
            {
                pictures.Add(new(Picture, 120, 120));
                PictureCounter += Speed;
                if (PictureCounter >= Math.Max(1, (int)((long)(Picture == 9 ? 793 : 721) * scrollLimit / 9120)))
                {
                    Picture++;
                    PictureCounter = 0;
                    transition = true;
                }
            }
        }

        pictures.Add(new(animationFrame, 296, 330));
        if (++animationCounter > 4)
        {
            animationCounter = 0;
            if (++animationFrame > 12)
            {
                animationFrame = 10;
            }
        }

        int y = TextY;
        if (Scroll < scrollLimit)
        {
            TextY -= Speed;
            Scroll += Speed;
        }
        else
        {
            Completed = true;
        }

        return new(pictures.ToArray(), y);
    }
}
