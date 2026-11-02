using System.Reflection;
using System.Runtime.InteropServices;
using Atria.Game.Controls;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Platform.Input;
using Atria.Resources;

CoreBoundary();
InputEdges();
SaveCodec();
Console.WriteLine("Core-only checks passed.");

static void CoreBoundary()
{
    Assembly core = typeof(GameState).Assembly;
    if (core.GetReferencedAssemblies().Any(reference => reference.Name is "Atria" or "Atria.Windows"))
        throw new InvalidOperationException("Core references the Windows executable.");

    foreach (Type type in core.GetTypes())
    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0 || method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName is "System.Runtime.InteropServices.DllImportAttribute" or "System.Runtime.InteropServices.LibraryImportAttribute"))
            throw new InvalidOperationException($"Core declares a native import: {type.FullName}.{method.Name}");
    }
}

static void InputEdges()
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

static void SaveCodec()
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

sealed class DisconnectedGamepads : IGamepadDevice
{
    public PadSample Read(int slot) => default;
    public void Vibrate(int slot, ushort left, ushort right) { }
}
