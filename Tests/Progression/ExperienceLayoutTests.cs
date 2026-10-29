using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Tests.Render;

namespace Atria.Tests.Progression;

public static class ExperienceLayoutTests
{
    public static string Run(string root)
    {
        var previous = GameText.Current;
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        var tiles = SpriteFile.Decode(archive.ReadMember(archive.FindResource("ETC"u8)));
        var labels = GameText.Messages(LegacyTextTables.ParseBattle(File.ReadAllBytes(Path.Combine(root, "BATTLE.DAT"))), "battle");
        var raster = new ExperienceAllocationRaster(tiles, font, labels, X87Precision.Bits53);
        var state = new GameState();
        int[] levels = [1, 9, 10, 11, 12, 13];
        int[] experience = [0, 250, 500, 750, 1000, 1500];
        int[] awards = [0, 250, 250, 750, 0, 1000];
        ushort[] pixels = new ushort[640 * 480], expectedText = new ushort[pixels.Length];
        string output = Path.GetFullPath(Path.Combine(root, "..", "audit", "xp-0101"));
        Directory.CreateDirectory(output);
        try
        {
            foreach (string language in new[] { "ru-RU", "be-BY" })
            {
                GameText.Use(new LocalizationCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "localization"), language));
                for (int actor = 0; actor < 6; actor++)
                {
                    state.Party[actor].MemberStatus = 1;
                    state.Party[actor].Level = levels[actor];
                    state.Party[actor].Experience = experience[actor];
                    state.Party[actor].NextExperience = 1000;
                }

                Array.Clear(pixels);
                raster.Draw(state.Heroes, awards, 3, 2, 1110, pixels);
                for (int actor = 0; actor < 6; actor++)
                {
                    int top = 129 + actor * 22;
                    Array.Clear(expectedText);
                    string label = GameText.Format("ui.progression.level", ("level", levels[actor].ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    Check(207 + font.MeasureText(label) <= 264, language + " level label leaves an eight-pixel gap");
                    font.DrawText(label, expectedText, 640, 480, 640, 207, top, 0xffe0);
                    for (int y = top; y < top + 16; y++)
                    for (int x = 207; x < 272; x++)
                        if (expectedText[y * 640 + x] != 0)
                            Check(pixels[y * 640 + x] == expectedText[y * 640 + x], language + " complete level label survives bar rendering");

                    CheckBar(actor, experience[actor], awards[actor]);
                }

                UnicodeTextTests.SaveBitmap(Path.Combine(output, language + ".bmp"), pixels);

                foreach (int level in Enumerable.Range(1, 13))
                foreach (int xp in new[] { int.MinValue, -1, 0, 500, 1000, int.MaxValue })
                foreach (int award in new[] { 0, 250, int.MaxValue })
                {
                    state.Party[0].Level = level;
                    state.Party[0].Experience = xp;
                    awards[0] = award;
                    Array.Clear(pixels);
                    raster.Draw(state.Heroes, awards, 0, 0, 0, pixels);
                    CheckBar(0, xp, award);
                }
                awards[0] = 0;
            }
        }
        finally
        {
            GameText.Use(previous);
        }
        return "XP layout: ru/be level labels, ranks1..13, proportional earned/award/remaining spans and negative/overflow XP bounds passed. Captures: " + output;

        void CheckBar(int actor, int xp, int award)
        {
            const int barLeft = 272, barRight = 407, barWidth = 135;
            int earned = (int)Math.Clamp(Math.Truncate((double)barWidth / 1000 * xp), 0, barWidth);
            int added = (int)Math.Clamp(Math.Truncate((double)barWidth / 1000 * award), 0, barWidth - earned);
            int y = 131 + actor * 22;
            for (int x = barLeft; x < barRight; x++)
            {
                ushort expected = x < barLeft + earned ? (ushort)0x739f : x < barLeft + earned + added ? (ushort)0xffee : (ushort)0xfb8e;
                Check(pixels[y * 640 + x] == expected, "XP proportional segment color");
            }
            Check(pixels[y * 640 + barLeft - 1] == 0 && pixels[y * 640 + barRight] == 0, "XP fill stays inside both bar boundaries");
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
