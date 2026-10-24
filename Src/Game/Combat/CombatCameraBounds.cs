using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>401800/401890 scroll counters for a real loaded bitmap layer.</summary>
public sealed class CombatScrollLayer(int width, int height, int horizontalDivisor = 1, int verticalDivisor = 1)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int X
    {
        get; private set;
    }
    public int Y
    {
        get; private set;
    }

    private int horizontalCounter, verticalCounter;
    public bool MoveX(int delta) => Move(delta, Width, 639, horizontalDivisor, ref horizontalCounter, true);
    public bool MoveY(int delta) => Move(delta, Height, 400, verticalDivisor, ref verticalCounter, false);
    private bool Move(int delta, int extent, int view, int divisor, ref int counter, bool horizontal)
    {
        int position = horizontal ? X : Y;
        if (delta < 0)
        {
            if (position < 1)
            {
                return false;
            }

            counter = unchecked(counter - 1);
            if (counter < 1)
            {
                position = Math.Max(0, unchecked(position + delta));
                counter = divisor;
            }
        }
        else
        {
            if (extent <= unchecked(view + position))
            {
                return false;
            }

            counter = unchecked(counter + 1);
            if (divisor <= counter)
            {
                position = unchecked(position + delta);
                counter = 0;
                if (extent < unchecked(position + view))
                {
                    position = extent - view;
                }
            }
        }

        if (horizontal)
        {
            X = position;
        }
        else
        {
            Y = position;
        }

        return true;
    }
}

/// <summary>4078b0 camera scrolling/actor bounds plus4077f0 world and particle translation.</summary>
public sealed class CombatCameraBounds(CombatSceneRuntime runtime, CombatScrollLayer primary, Action<int, int> translateParticles, CombatScrollLayer? secondary = null)
{
    public CombatScrollLayer Primary { get; } = primary;
    public CombatScrollLayer? Secondary { get; } = secondary;

    public void Update(int index)
    {
        CombatActorImage image = runtime.Actors[index];
        CombatActorState actor = image.State;
        if (image.Animation is null)
        {
            throw new InvalidOperationException("Camera actor has no actual bound sprite.");
        }

        if (actor.ControlledBy == 1)
        {
            if (actor.SpriteX < 150)
            {
                if (Primary.MoveX(-2))
                {
                    Translate(2, 0);
                    Secondary?.MoveX(-2);
                }
            }
            else if (actor.SpriteX > 400 && Primary.MoveX(2))
            {
                Translate(-2, 0);
                Secondary?.MoveX(2);
            }

            if (actor.Depth < 371)
            {
                if (actor.Depth <= 269 && Primary.MoveY(-2))
                {
                    Translate(0, 2);
                    Secondary?.MoveY(-2);
                }
            }
            else if (Primary.MoveY(2))
            {
                Translate(0, -2);
                Secondary?.MoveY(2);
            }
        }

        var sprite = image.Animation.CurrentSprite;
        int metadata = sprite.Metadata2;
        int left = actor.ControlledBy == 1 ? 0 : -80;
        int right = unchecked((actor.ControlledBy == 1 ? Primary.Width : Primary.Width + 80) - Primary.X);
        bool forward = actor.Facing == 1;
        int center = unchecked(actor.SpriteX + (forward ? metadata : sprite.Width - metadata));
        int offset = forward ? -metadata : metadata - sprite.Width;
        if (center < left)
        {
            actor.SpriteX = unchecked(left + offset);
            actor.AnchorX = unchecked(left + offset);
            if (actor.ActionFlags == 1 && unchecked((sbyte)actor.Facing) == -1)
            {
                actor.MovementTicksX = 0;
            }
        }
        else if (center > right)
        {
            actor.SpriteX = unchecked(right + offset);
            actor.AnchorX = unchecked(right + offset);
            if (actor.ActionFlags == 1 && actor.Facing == 1)
            {
                actor.MovementTicksX = 0;
            }

            if (unchecked((sbyte)actor.Facing) == -1)
            {
                actor.AnchorX = unchecked(actor.AnchorX + sprite.Width);
            }
        }

        int worldY = unchecked(Primary.Y + actor.Depth);
        int bound = worldY > runtime.Scene.Header77 ? runtime.Scene.Header77 : worldY < runtime.Scene.Header73 ? runtime.Scene.Header73 : worldY;
        if (bound != worldY)
        {
            int y = unchecked(bound - Primary.Y);
            actor.SpriteY = unchecked(actor.SpriteY + y - actor.Depth);
            actor.Depth = y;
            actor.MovementTicksX = 0;
            actor.MovementTicksY = 0;
        }
    }

    private void Translate(int dx, int dy)
    {
        foreach (CombatActorImage image in runtime.Actors)
        {
            CombatActorState a = image.State;
            if (a.Active == 0)
            {
                continue;
            }

            a.AnchorX = unchecked(a.AnchorX + dx);
            a.SpriteX = unchecked(a.SpriteX + dx);
            a.AnchorY = unchecked(a.AnchorY + dy);
            a.Depth = unchecked(a.Depth + dy);
            a.SpriteY = unchecked(a.SpriteY + dy);
        }

        translateParticles(dx, dy);
    }

}
