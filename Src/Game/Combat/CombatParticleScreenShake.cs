namespace Atria.Game.Combat;

/// <summary>405f70 final-ANI-DWORD flags, consumed by40a820 background copy stage.</summary>
public sealed class CombatParticleScreenShake
{
    public int X
    {
        get; private set;
    }
    public int Y
    {
        get; set;
    } //45a874: preserved by405f70; future proven vertical setters share this bank.
    public int Duration
    {
        get; private set;
    }
    public int Counter
    {
        get; private set;
    }

    public void Apply(uint flags)
    {
        if ((flags & 0x100) != 0)
        {
            X = 5;
            Duration = 10;
        }
        else if ((flags & 0x200) != 0)
        {
            X = 10;
            Duration = 15;
        }
        else if ((flags & 0x400) != 0)
        {
            X = 15;
            Duration = 20;
        }
        else if ((flags & 0x800) != 0)
        {
            X = 20;
            Duration = 25;
        }
        else
        {
            return;
        }

        Counter = 0;
    }

    public (int X, int Y) AdvanceBackground()
    {
        if (Duration == 0)
        {
            return (0, 0);
        }

        Counter = unchecked(Counter + 1);
        var result = (Counter & 3) < 2 ? (X, Y) : (0, 0);
        if (Duration <= Counter)
        {
            X = Y = Duration = Counter = 0;
        }

        return result;
    }
}
