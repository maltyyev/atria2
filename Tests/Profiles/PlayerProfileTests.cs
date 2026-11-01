using Atria.Platform.Input;
using Atria.Game.Controls;
using Atria.Game.Persistence;
using Atria.Game.Profiles;
using Atria.Platform.Win32;
using Atria.Resources;

namespace Atria.Tests.Profiles;

public static class PlayerProfileTests
{
    private sealed class Pads : IGamepadDevice
    {
        public ushort Buttons;
        public PadSample Read(int slot) => new(slot == 0, Buttons, 0, 0, 0, 0, 0, 0);
        public void Vibrate(int slot, ushort left, ushort right) { }
    }

    public static string Run(string resourceRoot)
    {
        string root = Path.Combine(Path.GetTempPath(), "AtriaProfiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "saves"));
        File.WriteAllText(Path.Combine(root, "config", "preferences.json"), "legacy-settings");
        File.WriteAllBytes(Path.Combine(root, "saves", "atria005.sav"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(root, "saves", "auto003.sav"), [4, 5, 6]);
        string Default(long number) => "Профиль" + number;
        var store = new PlayerProfileStore(root, Default);
        var first = store.Active;
        string original = store.DirectoryFor(first);
        Check(first.Name == "Профиль1" && File.ReadAllText(Path.Combine(original, "config", "preferences.json")) == "legacy-settings",
            "existing settings migrate to first named profile");
        Check(File.ReadAllBytes(Path.Combine(original, "saves", "auto003.sav")).SequenceEqual(new byte[] { 4, 5, 6 }),
            "autosave copied byte for byte");
        Check(File.ReadAllBytes(Path.Combine(root, "saves", "atria005.sav")).SequenceEqual(new byte[] { 1, 2, 3 }),
            "migration preserves original recovery files");

        // Recover interruption after directory commit but before catalog commit.
        File.Delete(Path.Combine(root, "profiles.json"));
        store = new(root, Default);
        Check(store.Active.Id == first.Id, "migration retry keeps the original profile identity");
        var second = store.Create(store.NextName(Default));
        Check(second.Name == "Профиль2" && new NativeSaveStore(Path.Combine(store.DirectoryFor(second), "saves"), resourceRoot)
            .InspectLibrary(false).Length == 0, "new profile has no inherited saves");
        store.Rename(second.Id, "Святлана / Роланд");
        store.Select(second.Id);
        store = new(root, Default);
        Check(store.Active.Name == "Святлана / Роланд" && store.Active.Id == second.Id,
            "Unicode rename and selected profile survive restart; display name is not a path");
        File.WriteAllBytes(Path.Combine(original, "saves", "atria005.sav"), [9]);
        store = new(root, Default);
        Check(File.ReadAllBytes(Path.Combine(original, "saves", "atria005.sav")).SequenceEqual(new byte[] { 9 }),
            "later startups never reimport stale legacy saves");
        for (int i = 0; i < 40; i++) store.Create(store.NextName(Default));
        Check(new PlayerProfileStore(root, Default).Profiles.Count == 42, "no five-profile or one-screen cap");

        string catalog = Path.Combine(root, "profiles.json");
        string good = File.ReadAllText(catalog);
        File.WriteAllText(catalog, good.Replace(first.Id, "../escape"));
        bool rejected = false;
        try { _ = new PlayerProfileStore(root, Default); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(catalog).Contains("../escape"), "invalid IDs are rejected without overwriting the catalog");
        File.WriteAllText(catalog, good);
        var editor = new ProfileNameEditor("Профиль3");
        editor.Type("Святлана");
        editor.Accept();
        Check(editor.Result == "Святлана", "typing replaces the suggested name and preserves Cyrillic");
        editor = new("старое");
        editor.Type("е\u0301🙂");
        editor.Erase();
        Check(editor.Text == "е\u0301", "backspace erases a whole Unicode text element");
        editor = new("   ");
        editor.Accept();
        Check(!editor.Closed && editor.Result is null, "empty names stay in editor");

        var legacy = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(resourceRoot, "BATTLE.CFG")));
        var pads = new Pads();
        using var input = new InputRouter(ControlProfile.Defaults(legacy), legacy, pads);
        bool[] keys = new bool[256], pressed = new bool[256], mouse = new bool[3];
        long clock = 0;
        void Poll() => input.Poll(keys, pressed, mouse, true, clock++);
        Poll();
        keys[0x3c] = true; Poll();
        Check(input.ProfileMenuPressed() && !input.ProfileMenuPressed(), "F2 edge opens profiles once");
        keys[0x3c] = false; Poll();
        pads.Buttons = (ushort)GamepadButtons.Y; Poll();
        Check(input.ProfileMenuPressed(), "physical Xbox Y opens profiles");
        pads.Buttons = 0; Poll();
        editor = new("Профиль4");
        pads.Buttons = (ushort)GamepadButtons.Start; Poll();
        editor.Update(input, "");
        Check(editor.Result == "Профиль4", "controller can accept the default name without keyboard");
        editor = new("Профиль5");
        pads.Buttons = 0; Poll();
        pads.Buttons = (ushort)GamepadButtons.Y; Poll(); editor.Update(input, "");
        pads.Buttons = 0; Poll();
        pads.Buttons = (ushort)GamepadButtons.A; Poll(); editor.Update(input, "");
        pads.Buttons = 0; Poll();
        pads.Buttons = (ushort)GamepadButtons.Start; Poll(); editor.Update(input, "");
        Check(editor.Result == "а", "controller can change alphabet, enter a letter and save");
        return "Player profiles PASS: safe migration/retry, isolation, persistent selection/Unicode names, 42 profiles, invalid catalog preservation, F2/Y and name input.";
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("Player profiles: " + message);
    }
}
