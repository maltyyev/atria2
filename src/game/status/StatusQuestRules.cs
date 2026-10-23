using Atria.Game.State;
namespace Atria.Game.Status;
/// <summary>419dd0/419e20 immutable EXE tables44d23c/44d26c/44d29c/44d2cc.</summary>
public static class StatusQuestRules
{
    private static ReadOnlySpan<int> Ids=>[3,8,12,17,20,24,29,34,40,45,46,47];
    private static ReadOnlySpan<int> Events=>[2,18,18,18,28,28,28,41,41,41,45,45];
    private static ReadOnlySpan<int> Slots=>[2,1,1,1,1,1,1,1,1,1,2,2];
    private static ReadOnlySpan<int> Regions=>[0,1,1,1,2,2,2,3,3,3,4,4];
    public static int Stage(GameState state,int id){int index=Ids.IndexOf(id);return index<0?0:state.Events[Events[index]*10+Slots[index]]==1?1:2;}
    public static int Region(int id){int index=Ids.IndexOf(id);if(index<0)throw new InvalidDataException("Quest region is defined only for twelve native event items.");return Regions[index];}
}
