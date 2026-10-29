using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>40a820 actor/particle depth queue and actual world/menu raster; HUD is a separate stage.</summary>
public sealed class CombatSceneRenderer(CombatSceneRuntime runtime, CombatBattleAssets assets, CombatCameraBounds camera, CombatCameraShake shake)
{
    private readonly List<DrawItem> queue = new(runtime.Actors.Count);
    private readonly CombatBackgroundLayer primaryBackground = new(assets.BackgroundWidth, assets.BackgroundHeight, assets.Background);
    private int detail, shakeX;
    private readonly record struct DrawItem(int X, int Y, int Depth, int Code, SpriteFrame? Sprite, int Divisor, ushort Color);
    public void PrepareActors(int visualDetail, bool advanceActors = true)
    {
        queue.Clear();
        detail = visualDetail;
        shakeX = shake.AdvanceVisual();
        for (int actorIndex = 0; actorIndex < runtime.Actors.Count; actorIndex++)
        {
            CombatActorImage image = runtime.Actors[actorIndex];
            CombatActorState a = image.State;
            if (a.SpawnCondition != 0 || a.Active == 0)
            {
                continue;
            }

            if (image.Animation is null || image.Binding is null)
            {
                throw new InvalidOperationException("Visible combat actor lacks actual bindings.");
            }

            int code = unchecked((sbyte)a.Facing);
            if (a.Motion == 4)
            {
                code = -code;
            }

            int divisor = 0;
            ushort color = unchecked((ushort)a.FlashColor);
            if (advanceActors)
            {
                if (a.FlashTicks > 0)
                {
                    if ((a.FlashTicks & 15) == 15)
                    {
                        a.FlashPhase = a.FlashPhase == 0 ? 1 : 0;
                    }

                    if (a.FlashPhase != 0 && color != 0)
                    {
                        code = unchecked((sbyte)a.Facing) * 6;
                    }

                    a.FlashTicks = a.FlashTicks - 1;
                    if (a.FlashTicks == 0)
                    {
                        a.Invulnerable = 0;
                    }
                }

                if (a.Rank == 0)
                {
                    if (a.DeathFadeTicks != 0)
                    {
                        a.DeathFadeTicks = a.DeathFadeTicks - 1;
                    }

                    divisor = Math.Max(1, 5 - (a.DeathFadeTicks >> 3));
                    code = unchecked((sbyte)a.Facing) * 7;
                }

                if (a.Translucent != 0)
                {
                    code = unchecked((sbyte)a.Facing) * 5;
                }

                if (image.Binding.Info.Type > 45)
                {
                    code *= 6;
                    color = 0x4208;
                }
            }

            if (image.Binding.Info.Type is 13 or 25 or 26)
            {
                code = unchecked((sbyte)a.Facing);
                if (a.Motion == 4)
                {
                    code = -code;
                }

                code *= 5;
            }

            if (unchecked((sbyte)a.Team) > 99 && actorIndex < runtime.Scene.PartyStartIndex)
            {
                code *= 12;
            }

            if (a.Invulnerable > 10)
            {
                code *= 5;
            }

            queue.Add(new(a.SpriteX, a.SpriteY, a.Depth, code, image.Animation.CurrentSprite, divisor, color));
        }
    }

    public void AppendParticles(ReadOnlySpan<CombatParticleDraw> particles)
    {
        foreach (var p in particles)
        {
            queue.Add(new(p.X, p.Y, p.Depth, p.Code, p.Sprite, 0, 0));
        }

        for (int i = 0; i < queue.Count - 1; i++)
        {
            for (int j = i + 1; j < queue.Count; j++)
            {
                if (queue[j].Depth < queue[i].Depth)
                {
                    (queue[i], queue[j]) = (queue[j], queue[i]);
                }
            }
        }
    }

    public void DrawWorldAndMenu(ushort[] target)
    {
        if (target.Length != 640 * 480)
        {
            throw new ArgumentException("Combat target must be640x480.");
        }

        primaryBackground.Draw(target, camera.Primary, shakeX);
        if (assets.Secondary is not null)
        {
            assets.Secondary.Draw(target, camera.Secondary ?? throw new InvalidOperationException("Second battle bitmap requires its native scroll layer."));
        }

        assets.Menu.CopyTo(target, 400 * 640);
        RasterClip clip = new(0, 0, 639, 400);
        for (int i = 0; i < queue.Count; i++)
        {
            DrawItem item = queue[i];
            if (item.Y < 0)
            {
                continue;
            }

            if (item.Code is 999 or -999)
            {
                CombatSpecialRaster.DrawCrosshair(target, 640, clip);
                continue;
            }

            SpriteFrame sprite = item.Sprite ?? throw new NotSupportedException("Combat special null-sprite draw command requires its dedicated raster.");
            int code = Math.Abs(item.Code);
            bool mirrored = item.Code < 0;
            switch (code)
            {
                case 1:
                    if (detail < 2)
                    {
                        Shadow(item, sprite, target, mirrored, clip);
                    }

                    SpriteRaster.Draw565(sprite, target, 640, 480, 640, item.X, item.Y, detail == 0 ? SpriteBlend.HalfType3 : SpriteBlend.Copy, mirrored, clip);
                    break;
                case 6:
                    if (detail != 2)
                    {
                        Shadow(item, sprite, target, mirrored, clip);
                    }

                    CombatSpriteEffects.DrawTint(sprite, target, 640, item.X, item.Y, item.Color, mirrored, clip);
                    break;
                case 5:
                    CombatSpecialRaster.DrawHalfAll(sprite, target, 640, item.X, item.Y, mirrored, clip);
                    break;
                case 7:
                    CombatSpriteEffects.DrawFade(sprite, target, 640, item.X, item.Y, item.Divisor, mirrored, clip);
                    break;
                case 8:
                case 10:
                    SpriteRaster.Draw565(sprite, target, 640, 480, 640, item.X, item.Y, SpriteBlend.Copy, mirrored, clip);
                    if (code == 8 && item.Depth >= 0 && item.Depth < 400)
                    {
                        int left = Math.Clamp(item.X, 0, 639), right = Math.Clamp(unchecked(item.X + sprite.Width), 0, 639);
                        if (right > left)
                        {
                            target.AsSpan(item.Depth * 640 + left, right - left).Clear();
                        }
                    }

                    break;
                case 11:
                    CombatSpriteEffects.DrawAlpha(sprite, target, 640, item.X, item.Y, mirrored, clip);
                    break;
                case 12:
                    CombatSpriteEffects.DrawPacketColor(sprite, target, 640, item.X, item.Y, 0x7bef, mirrored, clip);
                    break;
                default:
                    if (detail < 2)
                    {
                        Shadow(item, sprite, target, mirrored, clip);
                    }

                    SpriteRaster.Draw565(sprite, target, 640, 480, 640, item.X, item.Y, detail == 0 ? SpriteBlend.HalfType3 : SpriteBlend.Copy, mirrored, clip);
                    break;
            }
        }
    }

    private static void Shadow(DrawItem item, SpriteFrame sprite, ushort[] target, bool mirrored, RasterClip clip) => CombatShadowRaster.Draw(sprite, target, 640, 480, 640, item.X, item.Depth - sprite.Height - 10, mirrored, clip);
}
