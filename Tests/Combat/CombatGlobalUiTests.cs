using System.Buffers.Binary;
using System.Text;
using Atria.Game.Combat;

namespace Atria.Tests.Combat;

public static class CombatGlobalUiTests
{
    public static string Run(string root)
    {
        var assets = CombatGlobalUiAssets.Load(root);
        var schedule = new CombatRenderSchedule(0);
        var ui = new CombatGlobalUiRuntime(schedule, assets.Messages);
        CombatActorState actor = new();
        var queried = new List<byte>();
        bool EandQ(byte scan)
        {
            queried.Add(scan);
            return scan is 0x12 or 0x10;
        }

        Check(ui.Advance(EandQ, 1, 1, actor) == 0 && schedule.Period == 25 && queried.SequenceEqual(new byte[] { 0x12, 0x10 }), "E and Q independently win their lazy branches; bounded speed still refreshes settings.");
        Check(schedule.SettingsNoticeDeadline == 5001 && ui.SettingsNoticeVisible, "Speed/detail share the same notice deadline.");
        queried.Clear();
        bool RandW(byte scan)
        {
            queried.Add(scan);
            return scan is 0x13 or 0x11;
        }

        Check(ui.Advance(RandW, 2, 0, actor) == 1 && schedule.Period == 45 && queried.SequenceEqual(new byte[] { 0x12, 0x13, 0x10, 0x11 }), "R/W are queried only when the preceding key returns false.");
        bool None(byte _) => false;
        ui.Advance(None, 5002, 1, actor);
        Check(!ui.SettingsNoticeVisible, "Settings notice excludes its exact deadline.");
        Check(ui.ShowRankNotice(3) && ui.NoticeText.Span.SequenceEqual(assets.Messages.Ranks[3].Span) && ui.NoticeX == 410 && ui.NoticeY == 70, "Rank notice selects the exact first BATTLE group and restarts the shared slider.");
        Check(!ui.ShowRankNotice(5) && ui.NoticePhase == 1, "Native out-of-range upper bound preserves existing notice.");
        ui.Advance(None, 0, 1, actor);
        Check(ui.NoticeX == 410, "Notice deadline equality does not move.");
        uint now = 1;
        int oldX = 410;
        while (ui.NoticePhase == 1)
        {
            oldX = ui.NoticeX;
            ui.Advance(None, now, 1, actor);
            if (ui.NoticePhase == 1)
            {
                int x = ui.NoticeX;
                ui.Advance(None, ui.NoticeDeadline, 1, actor);
                Check(ui.NoticeX == x, "Movement deadline comparison is strict.");
            }

            now += 31;
        }

        int threshold = (80 - assets.Messages.Ranks[3].Length) * 4;
        Check(ui.NoticePhase == 2 && ui.NoticeX <= threshold && oldX > threshold, "Slider stops on the first ten-pixel step crossing byte-length centering threshold.");
        uint dwell = ui.NoticeDeadline;
        int dwellX = ui.NoticeX;
        ui.Advance(None, dwell, 1, actor);
        Check(ui.NoticePhase == 2 && ui.NoticeX == dwellX, "Dwell includes deadline equality.");
        ui.Advance(None, dwell + 1, 1, actor);
        Check(ui.NoticePhase == 3 && ui.NoticeX == dwellX, "Dwell expiry switches phase without moving.");
        ui.Advance(None, dwell + 1, 1, actor);
        Check(ui.NoticeX == dwellX - 10, "Next exiting opportunity moves with the retained elapsed deadline.");
        now = dwell + 32;
        while (ui.NoticePhase != 0)
        {
            ui.Advance(None, now, 1, actor);
            now += 31;
        }

        Check(ui.NoticeX < 10, "Exiting notice clears only after passing x10.");
        Check(ui.ShowConsumedItemNotice(5) && ui.NoticeText.Span.SequenceEqual(assets.Messages.Items[5].Span) && ui.NoticeDeadline == 0, "Item notice replaces rank notice using BATTLE group1 slot5, resetting its deadline.");
        W(actor, 0x208 + 5 * 4, 53);
        W(actor, 0x174, 5);
        W(actor, 0x178, 3);
        W(actor, 0x375, 100);
        ui.Advance(None, 100, 1, actor);
        Check(I(actor, 0x178) == 3, "Item53 regeneration excludes deadline equality.");
        ui.Advance(None, 101, 1, actor);
        Check(I(actor, 0x178) == 4 && I(actor, 0x375) == 1101, "Item53 in sixth equipment slot restores exactly one MP and sets sampled time+1000.");
        ui.Advance(None, 1102, 1, actor);
        ui.Advance(None, 2103, 1, actor);
        Check(I(actor, 0x178) == 5 && I(actor, 0x375) == 3103, "Full MP still refreshes the regeneration deadline.");
        W(actor, 0x208 + 5 * 4, -1);
        ui.Advance(None, 4000, 1, actor);
        Check(I(actor, 0x375) == 3103, "No equipped item53 leaves its actor deadline untouched.");
        ushort[] image = new ushort[640 * 480];
        var raster = new CombatGlobalUiRaster(assets);
        int phase = ui.NoticePhase, xBefore = ui.NoticeX;
        raster.Draw(ui, true, true, image);
        ushort[] cached = image.ToArray();
        Array.Clear(image);
        raster.Draw(ui, true, true, image);
        Check(image.SequenceEqual(cached) && image.Any(p => p == 0xffe0) && phase == ui.NoticePhase && xBefore == ui.NoticeX, "Actual small-font automation/pause rendering repeats without advancing UI or MP state.");
        Array.Clear(image);
        raster.Draw(ui, true, false, image);
        Check(!image.SequenceEqual(cached), "Pause label follows the externally owned native pause flag.");
        string text = CombatGlobalUiRaster.FormatSettings(3, 225);
        Check(text == "Детализация 3  Интервал 225", "Localized settings retain distinct detail and interval values.");
        return "Combat global UI: lazy speed/detail priority, shared deadline, strict sliding/dwell phases, item53 actor MP, pause and actual BATTLE/ENG1 raster passed.";
    }

    private static int I(object a, int at) => LegacyActorFixture.ReadInt32LittleEndian(a.FixtureSpan(at, 4));
    private static void W(object a, int at, int value) => LegacyActorFixture.WriteInt32LittleEndian(a.FixtureSpan(at, 4), value);
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
