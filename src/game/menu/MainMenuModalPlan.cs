namespace Atria.Game.Menu;

// Raw native helper contract, not guessed text/font rendering. Addresses identify immutable evidence.
public enum ModalPrimitive { Fill, WindowTiles, TextOffsetX, TextOffsetY, TextColor, TextStyle, ShadowText, ForegroundText }
public readonly record struct ModalOperation(ModalPrimitive Kind, int X = 0, int Y = 0, int Width = 0,
    int Height = 0, uint DataAddress = 0);

public static class MainMenuModalPlan
{
    // Exact 0x423660 call ordering, recovered from native-ui-audio/selected.asm.
    // Host resolves raw string and style globals through its resource/text subsystem.
    public static ModalOperation[] Build(bool yes, int flash)
    {
        var result = new List<ModalOperation>(24)
        {
            new(ModalPrimitive.Fill, 174,174,292,100,0x4cc4c4),
            new(ModalPrimitive.WindowTiles,160,160,8,2),
            new(ModalPrimitive.TextOffsetX), new(ModalPrimitive.TextOffsetY),
            new(ModalPrimitive.TextColor, DataAddress:0x4cc480),
            new(ModalPrimitive.ShadowText,181,201,DataAddress:0x456b48),
            new(ModalPrimitive.TextStyle,DataAddress:0x4cc370),
            new(ModalPrimitive.ForegroundText,180,200,DataAddress:0x456b48),
            new(ModalPrimitive.TextStyle,DataAddress:0x4cc228)
        };
        if (yes) result.Add(new(ModalPrimitive.TextStyle,DataAddress:0x4cc484));
        result.Add(new(ModalPrimitive.TextColor,DataAddress:0x4cc480));
        result.Add(new(ModalPrimitive.ShadowText,251,237,DataAddress:0x456b98));
        if (yes && flash != 0)
        {
            result.Add(new(ModalPrimitive.TextColor,DataAddress:0x4cc3b0));
            result.Add(new(ModalPrimitive.ShadowText,250,236,DataAddress:0x456b98));
        }
        else result.Add(new(ModalPrimitive.ForegroundText,250,236,DataAddress:0x456b98));
        result.Add(new(ModalPrimitive.TextStyle,DataAddress:0x4cc228));
        if (!yes) result.Add(new(ModalPrimitive.TextStyle,DataAddress:0x4cc484));
        result.Add(new(ModalPrimitive.TextColor,DataAddress:0x4cc480));
        result.Add(new(ModalPrimitive.ShadowText,351,237,DataAddress:0x456be8));
        if (!yes && flash != 0)
        {
            result.Add(new(ModalPrimitive.TextColor,DataAddress:0x4cc3b0));
            result.Add(new(ModalPrimitive.ShadowText,350,236,DataAddress:0x456be8));
        }
        // Native no branch always draws foreground, including its flash case.
        result.Add(new(ModalPrimitive.ForegroundText,350,236,DataAddress:0x456be8));
        return result.ToArray();
    }
}
