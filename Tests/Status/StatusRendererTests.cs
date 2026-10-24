using Atria.Localization;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Status;

public static class StatusRendererTests
{
    public static void Run(string root)
    {
        void Check(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidOperationException("Status raster: " + message);
            }
        }

        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var journal = QuestJournalCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "DANGEL.DAT")));
        var eventText = File.ReadAllBytes(Path.Combine(root, "EITEM.DAT"));
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        SpriteFile Load(ReadOnlySpan<byte> name) => SpriteFile.Decode(archive.ReadMember(archive.FindResource(name)));
        var renderer = new StatusMenuRenderer(
            Load("item.spr"u8),
            Load("etc.spr"u8),
            Load("charinfo.spr"u8),
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng1.fnt")), true),
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng.fnt")), false),
            items,
            messages,
            File.ReadAllBytes(Path.Combine(root, "CHARINTR.DAT"))
        );
        var state = InitialGameState.Create(items, messages);
        state.SetQuestItemFlag(3, 1);
        state.Events[22] = 1;
        state.EventStatuses[1] = 1;
        state.EventStatuses[2] = 2;
        state.EventStatuses[3] = 3;
        Check(StatusQuestRules.Stage(state, 3) == 1 && StatusQuestRules.Region(3) == 0, "native quest3 event22 table");
        Check(StatusQuestRules.Stage(state, 8) == 2 && StatusQuestRules.Region(8) == 1 && StatusQuestRules.Stage(state, 0) == 0, "native mapped/unmapped quest rules");
        ushort[] pixels = new ushort[640 * 480];
        for (int page = 0; page < 5; page++)
        {
            var runtime = new StatusMenuRuntime();
            runtime.Enter(_ =>
            {
            });
            void Input(StatusMenuInput input) => runtime.Input(input, state, items, eventText, journal, _ =>
            {
            });
            Input(default);
            Input(default);
            for (int p = 0; p < page; p++)
            {
                Input(new(NextPage: true));
            }

            var rootPlan = renderer.BuildRootPlan(runtime, state, new());
            Check(rootPlan.Count(p => p.Primitive == StatusPrimitive.Panel) == 4, "four native root bordered panels");
            Check(!rootPlan.Any(p => p.Primitive == StatusPrimitive.Text && p.Text == GameText.Decode(messages[33].Span)), "removed skill is absent from character statistics");
            Check(rootPlan.Any(p => p.Primitive == StatusPrimitive.Portrait && p.X == 21 && p.Y == 30 && p.A == 0), "native actor portrait origin");
            Check(rootPlan.Any(p => p.Primitive == StatusPrimitive.Text && p.X == 24 && p.Y == 245 + page * 38 && p.Text == GameText.Decode(messages[12 + page].Span)), "native five page labels");
            Input(new(Accept: true));
            Check(runtime.Menu.DetailActive, "real detail initialization");
            var plan = renderer.BuildPlan(runtime, state, new(2), journal);
            Array.Fill(pixels, (ushort)0x39e7);
            renderer.DrawPlan(plan, new(2), pixels);
            if (page == 4)
            {
                byte[] active = System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1251)!.GetBytes("В работе");
                Check(plan.Any(p => p.Primitive == StatusPrimitive.Text && p.Color == 0x07ff && p.Text == GameText.Decode(active)), "active journal status is visibly drawn below the title");
                string output = Path.Combine(Path.GetFullPath(Path.Combine(root, "..")), "audit", "csharp-probe", "build-0.3.5-journal.rgb565");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                byte[] raster = new byte[pixels.Length * 2];
                Buffer.BlockCopy(pixels, 0, raster, 0, raster.Length);
                File.WriteAllBytes(output, raster);
            }

            if (page == 0)
            {
                Check(plan.Any(p => p.Primitive == StatusPrimitive.EtcSprite && p.X == 499 && p.Y == 25 && p.A == 24), "equipment cursor native origin");
                Input(new(Accept: true));
                plan = renderer.BuildPlan(runtime, state, new(), journal);
                Check(plan.Any(p => p.Primitive == StatusPrimitive.Panel && p.X == 288 && p.Y == 16 && p.A == 8 && p.B == 0), "equipment confirmation exact panel");
                Input(new(Accept: true));
                plan = renderer.BuildPlan(runtime, state, new(), journal);
                Check(runtime.Equipment.ListActive && plan.Any(p => p.Primitive == StatusPrimitive.Panel && p.X == 32 && p.Y == 288 && p.A == 16), "equipment uses native item list raster");
            }

            if (page == 1)
            {
                Check(plan.Any(p => p.Primitive == StatusPrimitive.ItemSprite && p.X == 72 && p.Y == 320), "item row icon native position");
                Input(new(Accept: true));
                plan = renderer.BuildPlan(runtime, state, new(), journal);
                Check(plan.Any(p => p.Primitive == StatusPrimitive.Fill && p.X == 62 && p.Y == 174 && p.A == 517 && p.B == 133), "item description inclusive fill rectangle");
            }

            if (page == 2)
            {
                Check(plan.Any(p => p.Primitive == StatusPrimitive.Text && p.X == 194 && p.Y == 225 && p.Text == GameText.Decode(messages[17].Span)), "base stat labels native column");
            }

            if (page == 3)
            {
                Check(plan.Any(p => p.Primitive == StatusPrimitive.Text && p.X == 136 && p.Y == 324 && p.Palette == 0x4cc400), "quest stage1 native cyan gradient");
            }

            if (page is 3 or 4)
            {
                Input(new(Accept: true));
                plan = renderer.BuildPlan(runtime, state, new(), journal);
                Check(plan.Any(p => p.Primitive == StatusPrimitive.Panel && p.X == 48 && p.Y == 160), "native quest/journal description panel");
            }

            Array.Fill(pixels, (ushort)0x39e7);
            renderer.DrawPlan(plan, new(), pixels);
        }
    }
}
