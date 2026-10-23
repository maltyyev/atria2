using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Status;

/// <summary>415010/4152a0/415330 lists;415580/416660/4168c0 three visible rows and descriptions.</summary>
public sealed class StatusTextList
{
    private readonly int[] ids;
    public IReadOnlyList<int> Ids=>ids;
    public int Scroll {get;private set;}
    public int Row {get;private set;}
    public bool DescriptionOpen {get;private set;}
    public int SelectedId=>ids.Length==0?throw new InvalidOperationException("Status list is empty."):ids[Scroll+Row];
    public StatusTextList(GameState state,int page)
    {
        ids=page switch
        {
            1=>Enumerable.Range(0,ItemCatalog.Count).Where(i=>state.OwnedItems[i]>0).ToArray(),
            3=>Enumerable.Range(0,51).Where(i=>state.GetQuestItemFlag(i)!=0).ToArray(),
            4=>Enumerable.Range(0,150).Where(i=>state.EventStatuses[i] is >=1 and <=4).ToArray(),
            _=>throw new ArgumentOutOfRangeException(nameof(page))
        };
    }
    // Returns true only when native handler leaves its detail page.
    public bool Input(int action,Action<int> sound,Action<int> loadDescription)
    {
        switch(action)
        {
            case 0:
                if(ids.Length>0&&!DescriptionOpen&&Scroll+Row<ids.Length-1){sound(1);if(Row<2)Row++;else Scroll++;}break;
            case 1:
                if(ids.Length>0&&!DescriptionOpen&&Scroll+Row>0){sound(1);if(Row>0)Row--;else Scroll--;}break;
            case 4:
                if(ids.Length>0){sound(3);if(DescriptionOpen)DescriptionOpen=false;else{loadDescription(SelectedId);DescriptionOpen=true;}}break;
            case 5:
                if(DescriptionOpen){sound(3);DescriptionOpen=false;}else{sound(4);return true;}break;
        }
        return false;
    }
    public static ReadOnlyMemory<byte>[] LoadDescription(int page,int id,ItemCatalog items,ReadOnlyMemory<byte> eventItemText,QuestJournalCatalog journal)
        =>Enumerable.Range(0,5).Select(line=>page switch
        {
            1=>items.GetDescriptionLine(id,line),
            3=>DatTables.GetEventItemDescriptionLine(eventItemText,id,line),
            4=>journal.GetDescriptionLine(id,line),
            _=>throw new ArgumentOutOfRangeException(nameof(page))
        }).ToArray();
}
