using Atria.Core.Contracts.Input;
using Atria.Core.Game.State;
using Atria.Core.Resources;
using Atria.Core.Runtime.Input;
using Atria.Core.Persistence;

namespace Tests.Core;

public static class CoreProbeChecks
{
    public static void Run()
    {
        InputEdges();
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
