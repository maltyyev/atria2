using System.Buffers.Binary;
using Atria.Game.Town;
using Atria.Render.Codecs;

namespace Atria.Tests.Town;

public static class TownTests
{
    public static string Run(string root)
    {
        var assets = TownSceneAssets.Load(root, "01mercen.sod"u8);
        var controller = new TownController();
        var commands = new List<TownCommand>();
        controller.Enter(assets.Scene, assets.Objects, 1, 0, "01mercen.sod"u8.ToArray(), "town"u8.ToArray(), TownCatalog.Get(1).MusicTrack, commands.Add);
        Check(commands.Any(c => c.Kind == TownEffectKind.QueueEnterScript), "first town queues enter");
        Check(commands[^2].Kind == TownEffectKind.LoadBackground && commands[^1].Kind == TownEffectKind.PlayMusic, "mode400 background then music");
        commands.Clear();
        controller.Input(new(Next: true), false, commands.Add);
        Check(controller.Selection == 0 && commands.Count == 0, "entry suppresses exactly first input");
        controller.Input(new(Next: true, Previous: true), false, commands.Add);
        Check(controller.Selection == 0 && commands.Count(c => c.Kind == TownEffectKind.Sound) == 2, "both directions run sequentially");
        commands.Clear();
        controller.Input(new(Previous: true), false, commands.Add);
        Check(controller.Selection == Atria.Render.Scenes.SodScenePresentation.SelectableCount(assets.Scene) - 1, "backwards wraps selectable count");
        controller.Input(new(Accept: true, Cancel: true), false, commands.Add);
        Check(!controller.LeaveConfirmation && commands[^1].Kind == TownEffectKind.OpenActionMenu && commands[^1].Value == 5, "accept precedes cancel and opens menu");
        commands.Clear();
        controller.ChooseAction(0, commands.Add);
        Check(commands[0].Kind == TownEffectKind.CloseActionMenus && commands[1].Kind == TownEffectKind.RunPreEnter, "enter script suspends before transition");
        commands.Clear();
        controller.CompletePreEnter(false, 400, commands.Add);
        Check(commands.Count == 0, "denied pre_enter stays");
        controller.Input(new(Cancel: true), false, commands.Add);
        Check(controller.LeaveYes, "leave defaults yes");
        controller.Input(new(ChooseNo: true), false, commands.Add);
        var no = controller.AdvanceLogicalFrame(new());
        Check(!no.LeaveYes && no.LeaveFlash == 2, "leave no flashes");
        controller.Input(new(Accept: true), false, commands.Add);
        Check(!controller.LeaveConfirmation, "leave no closes");
        controller.Input(new(Cancel: true), false, commands.Add);
        commands.Clear();
        controller.Input(new(Accept: true), false, commands.Add);
        Check(commands[^1].Kind == TownEffectKind.RunPreOut, "leave yes waits for pre_out");
        commands.Clear();
        controller.CompletePreOut(false, commands.Add);
        Check(commands.Count == 0, "denied pre_out stays");
        controller.Input(new(Cancel: true), false, commands.Add);
        controller.Input(new(Accept: true), false, commands.Add);
        commands.Clear();
        controller.CompletePreOut(true, commands.Add);
        Check(commands.Select(c => c.Kind).SequenceEqual([TownEffectKind.ReleaseTownSprite, TownEffectKind.CopyReturnMapReferences, TownEffectKind.LoadReturnMap, TownEffectKind.ResetModeStackAndEnableSave, TownEffectKind.ChangeMode]), "leave output order");
        Check(commands[^1].Value == 300, "return map mode");
        var animation = new TownController();
        animation.Enter(assets.Scene, assets.Objects, 1, 1, "01mercen.sod"u8.ToArray(), default, 10, _ =>
        {
        });
        Check(animation.AdvanceLogicalFrame(new()).MotionX == 0 && animation.AdvanceLogicalFrame(new()).MotionX == 0
&& animation.AdvanceLogicalFrame(new()).MotionX == 1, "marker advances after second draw");
        var suppressed = animation.AdvanceLogicalFrame(new(ActionMenuDepth: 0, LabelDelay: 0));
        Check(!suppressed.DrawLabel && suppressed.LabelDelayAfterDraw == -1, "native label suppression decrements delay below zero");
        for (int i = 0; i < 150; i++)
        {
            Check(animation.AdvanceLogicalFrame(new()).EffectFrames.Length == 5, "effect cursors remain five slots");
        }

        commands.Clear();
        animation.Input(new(), false, commands.Add);
        animation.Input(new(), true, commands.Add);
        Check(commands[^1] == new TownCommand(TownEffectKind.ChangeMode, 330), "encounter mode330");
        Check(commands.Any(c => c.Kind == TownEffectKind.SetPreviousTown && c.Value == 0), "encounter resets previous town");
        foreach (int kind in new[]
        {
            0,
            1,
            2
        }
        )
        {
            byte[] data = new byte[120 + 370];
            "Object Tool ver2.0"u8.CopyTo(data);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(30), 1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(73), 1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(120 + 28), kind);
            var scene = Atria.Resources.SodV2.Parse(data);
            var sprite = new SpriteFile(false, [new(0, 0, 0, [], [], 32, 32, [])]);
            var entry = new TownController();
            entry.Enter(scene, sprite, 0, 0, default, default, 1, _ =>
            {
            });
            commands.Clear();
            entry.ChooseAction(0, commands.Add);
            commands.Clear();
            entry.CompletePreEnter(true, 400, commands.Add);
            Check(commands[^1] == new TownCommand(TownEffectKind.ChangeMode, 410 + kind * 10), "object kind destination");
        }

        using var spritesArchive = Atria.Resources.BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        using var animationArchive = Atria.Resources.BinderArchive.Open(Path.Combine(root, "ANI.000"));
        var etc = SpriteFile.Decode(spritesArchive.ReadMember(spritesArchive.FindExact("ETC"u8)));
        var effects = SpriteFile.Decode(spritesArchive.ReadMember(spritesArchive.FindExact("CON_EFF"u8)));
        var effectAnimation = AnimationFile.Decode(animationArchive.ReadMember(animationArchive.FindExact("CON_EFF"u8)));
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng1.fnt")), true);
        var leaveRenderer = new TownLeaveModalRenderer(etc, font, File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        ushort[] target = new ushort[640 * 480];
        foreach (bool yes in new[]
        {
            true,
            false
        }
        )
        {
            foreach (int flash in new[]
            {
                0,
                1,
                2
            }
            )
            {
                var frame = new TownFrame(0, true, true, false, 0, 0, 0, true, yes, flash, new int[5]);
                TownComposer.Draw(assets, frame, 1, etc, font, effectAnimation, effects, target, leaveRenderer.Draw);
                Check(target.Where((p, i) => p != assets.Background565[i]).Any(), "actual town+leave composition");
            }
        }

        return "Town traces passed (real scene load, ordered inputs, entry guard, pre-enter/out gates, cursor/effects, encounter, three object destinations).";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception("Town: " + message);
        }
    }
}
