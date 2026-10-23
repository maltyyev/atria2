namespace Atria.Game.Dialogue;

public readonly record struct DialogueRuntimeSnapshot(bool QuestionInput,int PanelX,int Selection);
public sealed partial class DialogueRuntime
{
    public DialogueRuntimeSnapshot CaptureRuntime()
    {
        if(active)throw new NotSupportedException("Active dialogue continuation is not part of the save context.");
        return new(questionInput,panelX,selection);
    }
    public static void ValidateRuntime(DialogueRuntimeSnapshot value)
    {
        if(value.PanelX is not (0 or 80 or 160) || value.Selection<0)
            throw new InvalidDataException("Retained dialogue placement/selection is invalid.");
    }
    public void RestoreRuntime(DialogueRuntimeSnapshot value)
    {
        if(active)throw new InvalidOperationException("Cannot replace retained state during active dialogue.");
        ValidateRuntime(value);questionInput=value.QuestionInput;panelX=value.PanelX;selection=value.Selection;
    }
}
