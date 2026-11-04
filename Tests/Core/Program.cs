using Atria.Core.Contracts.Input;
using Atria.Core.Game.State;
using Atria.Core.Persistence;
using Atria.Core.Persistence.Profiles;
using Atria.Core.Presentation.Controls;
using Atria.Core.Presentation.Profiles;
using Atria.Core.Resources;
using Atria.Core.Runtime;
using Atria.Core.Runtime.Input;

namespace Tests.Core;

public static class CoreProbeChecks
{
    public static void Run()
    {
        InputEdges();
        MenuRowActions();
        MobileProfileNameAndStaleAction();
        SaveCodec();
        Console.WriteLine("Core-only checks passed.");
    }

    private static void InputEdges()
    {
        var legacy = ControlBindings.Parse(new byte[64]);
        var profile = new ControlProfile();
        profile.Players[0].PadSlot = -1;
        profile.Players[0].Set(GameAction.Attack, InputBinding.Key(30));
        using var input = new InputRouter(profile, legacy, new DisconnectedGamepads());
        var keys = new bool[256];
        var pressed = new bool[256];
        var mouse = new bool[3];

        keys[30] = true;
        input.Poll(keys, pressed, mouse, true, 1);
        if (!input.Press(0, GameAction.Attack) || input.Press(0, GameAction.Attack))
            throw new InvalidOperationException("Input edge was not consumed once.");
        keys[30] = false;
        input.Poll(keys, pressed, mouse, true, 2);
        keys[30] = true;
        input.Poll(keys, pressed, mouse, true, 3);
        if (!input.Press(0, GameAction.Attack))
            throw new InvalidOperationException("Input edge did not re-arm after release.");
    }

    private static void SaveCodec()
    {
        var state = new GameState { Money = 1234, Score = 5678, Mode = 300 };
        state.Globals[999] = -123;
        state.Events[999] = 456;
        byte[] image = NativeGameStateCodec.Encode(state);
        GameState decoded = NativeGameStateCodec.Decode(image);
        if (decoded.Money != state.Money || decoded.Score != state.Score || decoded.Mode != state.Mode || decoded.Globals[999] != -123 || decoded.Events[999] != 456)
            throw new InvalidOperationException("Core save codec did not preserve representative state.");

        image[0] = 0;
        try
        {
            NativeGameStateCodec.Decode(image);
            throw new InvalidOperationException("Core save codec accepted an invalid scalar schema.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void MenuRowActions()
    {
        var panel = new ControlPanel(null!, null!);
        int value = 0;
        ControlRow[] rows =
        [
            new("Action", "", () => value = 7, Id: "action"),
            new("Selector", "0", Adjust: direction => value += direction, Id: "selector")
        ];

        if (!panel.SelectRow(rows, "selector") || panel.Selection != 1
            || !panel.AdjustRow(rows, "selector", 1) || value != 1
            || !panel.ActivateRow(rows, "selector") || value != 2
            || !panel.ActivateRow(rows, "action") || value != 7
            || panel.ActivateRow(rows, "") || panel.SelectRow(rows, "missing"))
            throw new InvalidOperationException("Control panel stable row actions failed.");

        var longRows = Enumerable.Range(0, 15).Select(i => new ControlRow("Row", "", Id: $"row.{i}")).ToArray();
        if (!panel.SelectRow(longRows, "row.14") || panel.Selection != 14 || panel.Scroll != 3)
            throw new InvalidOperationException("Control panel row selection did not keep the selected row visible.");

        try
        {
            panel.AdjustRow(rows, "selector", 0);
            throw new InvalidOperationException("Control panel accepted a non-step adjustment.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static void MobileProfileNameAndStaleAction()
    {
        string directory = Path.Combine(Path.GetTempPath(), "atria-mobile-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profiles = new PlayerProfileStore(directory, number => "Profile" + number);
            int initialCount = profiles.Profiles.Count;
            string initialName = profiles.Active.Name;

            var editor = new ProfileNameEditor(initialName);
            editor.ReplaceText("");
            editor.Accept();
            Check(!editor.Closed && editor.Text == "" && editor.Error.Length > 0,
                "an empty native profile submit replaces the previous name and keeps the editor open");
            editor.ReplaceText("   ");
            editor.Accept();
            Check(!editor.Closed && editor.Text == "   ",
                "a whitespace-only native profile submit remains visible for correction");

            string fortyGraphemes = string.Concat(Enumerable.Repeat("я\u0301", PlayerProfileStore.MaximumNameLength));
            editor.ReplaceText(fortyGraphemes);
            editor.Accept();
            Check(editor.Closed && editor.Result is not null
                && new System.Globalization.StringInfo(editor.Result).LengthInTextElements == PlayerProfileStore.MaximumNameLength,
                "a Unicode name at the forty-grapheme limit is accepted");

            editor = new ProfileNameEditor("Old name");
            string tooLong = string.Concat(Enumerable.Repeat("я\u0301", PlayerProfileStore.MaximumNameLength + 1));
            editor.ReplaceText(tooLong);
            editor.Accept();
            Check(!editor.Closed && editor.Text == tooLong && editor.Error.Length > 0,
                "an overlong Unicode submit keeps the full field value and editor open");
            editor.ReplaceText("Ёлка 🌿");
            editor.Accept();
            Check(editor.Closed && editor.Result == "Ёлка 🌿",
                "a full-text Unicode profile submit preserves valid non-ASCII text");

            editor = new ProfileNameEditor("Do not write");
            var bindings = ControlBindings.Parse(new byte[64]);
            using (var input = new InputRouter(ControlProfile.Defaults(bindings), bindings, new DisconnectedGamepads()))
            {
                var keys = new bool[256];
                var pressed = new bool[256];
                keys[1] = true;
                input.Poll(keys, pressed, new bool[3], true, 1);
                editor.Update(input, "");
            }

            Check(editor.Closed && editor.Result is null && profiles.Profiles.Count == initialCount
                && profiles.Active.Name == initialName,
                "canceling profile name entry closes without writing profile data");

            var controller = new SaveLoadMenuController();
            var emitted = new List<SaveLoadMenuCommand>();
            controller.Enter(SaveLoadMenuKind.Save, 0, emitted.Add);
            controller.SetCount(1);
            var slots = new[] { LegacySlotMetadata.Empty(ReadOnlySpan<byte>.Empty) };
            controller.Input(new(Accept: true), slots, emitted.Add); // First Activate opens confirmation.
            var guard = new MobileMenuCommandGuard();
            const ulong staleContext = 41;
            Check(guard.Allows(staleContext) && controller.Confirmation,
                "the first save-row tap enters confirmation in its original context");
            guard.Invalidate(staleContext); // The modal has opened before the new snapshot is published.
            if (guard.Allows(staleContext))
            {
                controller.Input(new(Accept: true), slots, emitted.Add);
            }

            Check(controller.Confirmation && !controller.PendingOperation
                && emitted.All(command => command.Kind != SaveLoadMenuEffect.SaveSlot),
                "a stale second Activate cannot confirm or write the selected save");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Mobile menu: " + message);
        }
    }

    private sealed class DisconnectedGamepads : IGamepadDevice
    {
        public PadSample Read(int slot) => default;
        public void Vibrate(int slot, ushort left, ushort right) { }
    }
}

internal static class Program
{
    private static void Main(string[] args)
    {
        CoreProbeChecks.Run();
        if (args.Length == 0)
        {
            return;
        }

        if (args.Length != 2)
        {
            throw new ArgumentException("Pass the original resource and music roots.");
        }

        PortableTestSuite.Run(args[0], args[1]);
    }
}
