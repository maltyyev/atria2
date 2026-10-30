using Atria.Game.Dialogue;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Tests.Render;

namespace Atria.Tests.Combat;

internal static class NecromancerPresentationTests
{
    public static void Run(string root, GameState state)
    {
        var previous = GameText.Current;
        string output = Path.GetFullPath(Path.Combine(root, "..", "audit", "necromancer-0150"));
        Directory.CreateDirectory(output);
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        SpriteFile Sprite(ReadOnlySpan<byte> name) => SpriteFile.Decode(archive.ReadMember(archive.FindResource(name)));
        var tiles = Sprite("etc.spr"u8);
        var portraits = Sprite("charinfo.spr"u8);
        var additional = SpriteFile.Decode(File.ReadAllBytes(Path.Combine(root, "..", "interface", "EirenFaces.spr")));
        portraits = portraits with
        {
            Frames = [.. portraits.Frames, additional.Frames[3]]
        };
        var small = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var journal = QuestJournalCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "DANGEL.DAT")));
        var renderer = new StatusMenuRenderer(Sprite("item.spr"u8), tiles, portraits, small,
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false), items, messages,
            File.ReadAllBytes(Path.Combine(root, "CHARINTR.DAT")));
        try
        {
            foreach (string language in new[] { "ru-RU", "be-BY" })
            {
                GameText.Use(new LocalizationCatalog(Path.Combine(root, "..", "Localization"), language));
                for (int variant = 0; variant < 2; variant++)
                {
                    string key = variant == 0 ? "eiren.revised.unbind2.text" : "eiren.revised.free5.text";
                    var request = new DialogueRequest(1, 157 + variant, false, []) { TextKey = key, Paragraph = GameText.Get(key) };
                    var dialogue = new DialogueSession(request, false);
                    dialogue.PrepareLayout(small);
                    if (dialogue.Request.DisplayLines!.Any(line => small.MeasureText(line) > 440))
                    {
                        throw new InvalidOperationException("Authored dialogue exceeds its text panel.");
                    }

                    for (int i = 0; i < 8; i++)
                    {
                        dialogue.AdvancePresentation();
                    }

                    var pixels = Enumerable.Repeat((ushort)0x2925, 640 * 480).ToArray();
                    DialogueRaster.Draw(dialogue.Request, dialogue.AdvancePresentation(), tiles, additional.Frames[variant], small, pixels);
                    UnicodeTextTests.SaveBitmap(Path.Combine(output, $"{language}-portrait-{variant}.bmp"), pixels);
                }
                var status = new StatusMenuRuntime();
                status.Enter(_ => { });
                status.Menu.SetSelectedActor(state.HeroCatalog.Find("eiren_wald"));
                status.Menu.PointerPage(2);
                status.Input(default, state, items, default, journal, _ => { });
                status.Input(default, state, items, default, journal, _ => { });
                status.Input(new(Accept: true), state, items, default, journal, _ => { });
                var plan = renderer.BuildPlan(status, state, new(), journal);
                if (!plan.Any(command => command.Text == GameText.Get("ui.classes.necromancer")))
                {
                    throw new InvalidOperationException("Necromancer class is absent from the status page.");
                }

                var statusPixels = new ushort[640 * 480];
                renderer.DrawPlan(plan, new(), statusPixels);
                UnicodeTextTests.SaveBitmap(Path.Combine(output, $"{language}-status.bmp"), statusPixels);
            }
        }
        finally
        {
            GameText.Use(previous);
        }
    }
}
