using Atria.Game.State;

namespace Atria.Game.Interiors;

public sealed record InteriorTalkChoice(ReadOnlyMemory<byte> Label, ReadOnlyMemory<byte> ScriptMember, ReadOnlyMemory<byte> Function, bool IsEvent);
/// <summary>Owned talk choices from people_name and actual bounded event offer; choosing never invents a script result.</summary>
public sealed class InteriorTalkMenu
{
    public IReadOnlyList<InteriorTalkChoice> Choices
    {
        get;
    }

    private InteriorTalkMenu(InteriorTalkChoice[] choices) => Choices = Array.AsReadOnly(choices);
    public static InteriorTalkMenu Build(GameState state, ReadOnlyMemory<byte> currentScript, EventOffer? offer, IReadOnlyList<ReadOnlyMemory<byte>> messages)
    {
        if (state.People.Count > 20)
        {
            throw new InvalidDataException("Talk people exceed recovered20-slot registry.");
        }

        var choices = new List<InteriorTalkChoice>();
        foreach (var person in state.People)
        {
            choices.Add(new(GameText.Name(person.DisplayName), currentScript.ToArray(), person.Handler.ToArray(), false));
        }

        if (offer is not null)
        {
            int label = offer.Status switch
            {
                0 or 3 or 4 => 315,
                1 => 316,
                2 => 317,
                _ => throw new InvalidDataException("Event offer has no native talk label.")
            };
            if (messages.Count <= label)
            {
                throw new InvalidDataException("TEXTmsg lacks native event-offer labels.");
            }

            choices.Add(new(messages[label].ToArray(), offer.ScriptMember.ToArray(), offer.Function.ToArray(), true));
        }

        return new(choices.ToArray());
    }

    public InteriorTalkChoice Select(int index) => (uint)index < (uint)Choices.Count ? Choices[index] : throw new ArgumentOutOfRangeException(nameof(index));
}
