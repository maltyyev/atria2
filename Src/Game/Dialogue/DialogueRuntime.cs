using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Dialogue;

public interface IDialogueModalDriver
{
    /// <summary>Pump real rendering/input/audio until Completed. Never select an answer automatically.</summary>
    void Run(DialogueSession session, GameState state);
}

/// <summary>Dialogue portion of IScriptEffects. The containing engine routes other effects itself.</summary>
public sealed partial class DialogueRuntime(DialogueCatalog catalog, IDialogueModalDriver driver)
{
    private bool questionInput;
    private int panelX, selection;
    private bool active;
    public void Execute(ScriptEffect effect, GameState state)
    {
        if (active)
        {
            throw new InvalidOperationException("Nested dialogue overwrites the original singleton modal bank; reentry requires explicit engine handling.");
        }

        DialogueRequest request = DialogueRequest.Create(effect, catalog);
        var session = new DialogueSession(request, questionInput, panelX, selection);
        active = true;
        try
        {
            driver.Run(session, state);
            if (!session.Completed && !session.NativeLoopBypassed)
            {
                throw new InvalidOperationException("Dialogue driver returned before actual confirmation.");
            }
        }
        finally
        {
            questionInput = session.QuestionInputEnabled;
            panelX = session.PanelX;
            selection = session.Request.ChoiceByRow is { Length: > 0 } choices ? choices[session.SelectedLine] : session.SelectedLine;
            active = false;
        }
    }
}
