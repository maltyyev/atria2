using Atria.Core.Resources;

namespace Tests.Core.Platform;

public static class ClockTests
{
    public static string Run(string root)
    {
        var controls = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        if (controls.Confirm != 46 || controls.Cancel != 45 || controls.Previous != 203 || controls.Next != 205 || controls[1, 7] != 24)
        {
            throw new Exception("Original scan bindings changed.");
        }

        return "Platform bindings: original two-player scan codes passed.";
    }
}
