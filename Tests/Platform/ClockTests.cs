using Atria.Platform;
using Atria.Resources;

namespace Atria.Tests.Platform;

public static class ClockTests
{
    public static string Run(string root)
    {
        foreach (int hz in new[]
        {
            30,
            60,
            120,
            144,
            240
        }
        )
        {
            var clock = new LogicalFrameClock(TimeSpan.FromMilliseconds(21));
            for (int frame = 0; frame <= hz * 21; frame++)
            {
                while (clock.TakeDueFrame(TimeSpan.FromTicks(frame * TimeSpan.TicksPerSecond / hz)))
                {
                }
            }

            if (clock.Frames != 1001)
            {
                throw new Exception("Logical frames depend on present rate.");
            }
        }

        var stalled = new LogicalFrameClock(TimeSpan.FromMilliseconds(21));
        while (stalled.TakeDueFrame(TimeSpan.FromSeconds(21)))
        {
        }

        if (stalled.Frames != 1001)
        {
            throw new Exception("A stall discarded logical work.");
        }

        stalled.SuspendAt(TimeSpan.FromSeconds(30));
        if (stalled.TakeDueFrame(TimeSpan.FromSeconds(30)) || !stalled.TakeDueFrame(TimeSpan.FromMilliseconds(30021)))
        {
            throw new Exception("Suspension advances inactive simulation.");
        }

        var controls = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        if (controls.Confirm != 46 || controls.Cancel != 45 || controls.Previous != 203 || controls.Next != 205 || controls[1, 7] != 24)
        {
            throw new Exception("Original scan bindings changed.");
        }

        return "Clock: equal logical frames at30/60/120/144/240Hz and stalls; native two-player scan bindings passed. Not a combat timing assertion.";
    }
}
