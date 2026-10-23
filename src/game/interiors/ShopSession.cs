using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Interiors;

public sealed class ShopStock
{
    internal readonly int[] Counts=new int[3];
    internal readonly int[][] Ids=Enumerable.Range(0,3).Select(_=>new int[25]).ToArray();
    internal readonly int[][] Quantities=Enumerable.Range(0,3).Select(_=>new int[25]).ToArray();
    public static ShopStock FromCatalog(ShopCatalog catalog,int shop)
    {
        var stock=new ShopStock();
        for(int g=0;g<3;g++) {var source=catalog.GetGroup(shop,g);stock.Counts[g]=source.Count;for(int i=0;i<25;i++){stock.Ids[g][i]=source.GetItemId(i);stock.Quantities[g][i]=source.GetQuantity(i);}}
        return stock;
    }
    public int Count(int group)=>Counts[Group(group)];
    public int Item(int group,int row)=>Ids[Group(group)][Row(row)];
    public int Quantity(int group,int row)=>Quantities[Group(group)][Row(row)];
    public byte[] Encode()
    {
        var bytes=new byte[612];
        for(int g=0;g<3;g++) {Put(bytes,g*204,Counts[g]);for(int i=0;i<25;i++){Put(bytes,g*204+4+i*4,Ids[g][i]);Put(bytes,g*204+104+i*4,Quantities[g][i]);}}
        return bytes;
    }
    internal ShopStock Clone() {var copy=new ShopStock();CopyTo(copy);return copy;}
    internal void CopyTo(ShopStock target) {for(int g=0;g<3;g++){target.Counts[g]=Counts[g];Ids[g].CopyTo(target.Ids[g],0);Quantities[g].CopyTo(target.Quantities[g],0);}}
    private static void Put(byte[] b,int p,int v)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p,4),v);
    private static int Group(int g)=>(uint)g<3?g:throw new ArgumentOutOfRangeException(nameof(g));
    private static int Row(int r)=>(uint)r<25?r:throw new ArgumentOutOfRangeException(nameof(r));
}
public readonly record struct ShopInput(bool Confirm=false,bool Cancel=false,bool Left=false,bool Right=false,bool Up=false,bool Down=false,bool HeldUp=false,bool HeldDown=false);
public readonly record struct ShopInputResult(int Sound=-1,bool ReturnToActionMenu=false)
{ public ShopInputResult():this(-1,false) {} }
public readonly record struct ShopVisibleRow(int ItemId,int Quantity,int X,int Y,int SpriteFrame);

/// <summary>42ff60/430470 input, bounded25-row groups and original32-bit buy/sell arithmetic.</summary>
public sealed class ShopSession
{
    private readonly GameState state;
    private readonly ItemCatalog items;
    private ShopStock data=new();
    private ShopStock? persistent;
    private int repeatCounter;
    public bool Buying { get; private set; }
    public int Category { get; private set; }
    public int Row { get; private set; }
    public int Scroll { get; private set; }
    public int Quantity { get; private set; }
    public bool QuantityActive { get; private set; }
    public bool InsufficientFunds { get; private set; }
    public int Count=>data.Counts[Category];
    public int SelectedItemId=>Count>0&&Row>=0&&Row+Scroll<Count?data.Ids[Category][Row+Scroll]:-1;
    public void PointerSelect(int x,int y)
    {if(!QuantityActive&&x is >=130 and <490&&y is >=128 and <358){int row=(y-128)/46;if(row+Scroll<Count)Row=row;}}
    public ShopSession(GameState state,ItemCatalog items) {this.state=state;this.items=items;}
    public void BeginBuy(ShopStock stock)
    {
        persistent=stock;data=stock.Clone();Buying=true;ResetCursor();
        if(data.Counts[0]==0) Category=data.Counts[1]==0?2:1;
        ValidateActiveItems();
    }
    public void BeginSell()
    {
        persistent=null;data=new();Buying=false;ResetCursor();
        for(int g=0;g<3;g++) Array.Fill(data.Ids[g],-1);
        //42f0d0 end pointer47314e establishes88 item records, not all366 catalog records.
        for(int id=0;id<88;id++)
        {
            int quantity=state.AvailableItems[id];if(quantity<1) continue;
            int type=ItemInt(id,30),group=type switch {>=0 and <=4=>0,>=5 and <=7=>1,8 or 9=>2,_=>-1};
            if(group<0) continue;
            int row=data.Counts[group];if(row==25) throw new InvalidDataException("Sale list exceeds original25-entry group.");
            data.Ids[group][row]=id;data.Quantities[group][row]=quantity;data.Counts[group]++;
        }
    }
    private void ResetCursor(){Category=Row=Scroll=0;QuantityActive=InsufficientFunds=false;Quantity=0;}
    private void ValidateActiveItems(){for(int g=0;g<3;g++){if((uint)data.Counts[g]>25)throw new InvalidDataException("Shop count exceeds25 slots.");for(int i=0;i<data.Counts[g];i++) _=ItemInt(data.Ids[g][i],46);}}
    public IReadOnlyList<ShopVisibleRow> VisibleRows()
    {
        var rows=new List<ShopVisibleRow>();
        for(int i=0;i<5&&i+Scroll<Count;i++) rows.Add(new(data.Ids[Category][Scroll+i],data.Quantities[Category][Scroll+i],144,132+i*46,data.Ids[Category][Scroll+i]+10));
        return rows.AsReadOnly();
    }
    public ShopInputResult HandleInput(ShopInput input)
    {
        if(QuantityActive) return QuantityInput(input);
        if(input.Confirm)
        {
            if(Count<1 || Row<0 || Row+Scroll>=Count) return new(4);
            int id=data.Ids[Category][Row+Scroll],available=data.Quantities[Category][Row+Scroll];
            if(Buying?available<1:ItemInt(id,46)==0) return new(4);
            if(available!=0) {QuantityActive=true;Quantity=1;}
            return new(3);
        }
        if(input.Cancel) return new(4,true);
        if(input.Left||input.Right) {Category=(Category+(input.Left?2:1))%3;Row=Scroll=0;return new(1);}
        if(input.Up && Row+Scroll>0) {if(Row==0)Scroll--;else Row--;return new(1);}
        if(!input.Up && input.Down && Row+Scroll<Count-1) {if(Row==4)Scroll++;else Row++;return new(1);}
        return new();
    }
    private ShopInputResult QuantityInput(ShopInput input)
    {
        int index=Row+Scroll;
        if((uint)index>=(uint)Count) throw new InvalidDataException("Quantity selection no longer refers to a row.");
        int id=data.Ids[Category][index],available=data.Quantities[Category][index];
        if(input.Confirm)
        {
            if(Buying && InsufficientFunds) {InsufficientFunds=QuantityActive=false;return new(4);}
            int price=ItemInt(id,46),total=unchecked(price*Quantity);
            if(Buying && state.Money<total) {InsufficientFunds=true;return new();}
            data.Quantities[Category][index]=unchecked(available-Quantity);
            if(!Buying && data.Quantities[Category][index]<0) data.Quantities[Category][index]=0;
            CompactCurrentNative();
            if(Buying)
            {
                if(id<84) {state.OwnedItems[id]=unchecked(state.OwnedItems[id]+Quantity);state.AvailableItems[id]=unchecked(state.AvailableItems[id]+Quantity);}
                else if(id==84)state.ResourceCounterDc88=unchecked(state.ResourceCounterDc88+Quantity);
                else if(id==85)state.ResourceCounterDc88=unchecked(state.ResourceCounterDc88+Quantity*5);
                else if(id==86)state.ResourceCounterFc9a=unchecked(state.ResourceCounterFc9a+Quantity*5);
                else if(id==87)state.ResourceCounterFc9a=unchecked(state.ResourceCounterFc9a+Quantity*30);
                state.Money=unchecked(state.Money-total);
            }
            else
            {
                state.OwnedItems[id]=unchecked(state.OwnedItems[id]-Quantity);state.AvailableItems[id]=unchecked(state.AvailableItems[id]-Quantity);
                if(state.OwnedItems[id]<0) state.OwnedItems[id]=state.AvailableItems[id]=0;
                if(state.AvailableItems[id]<0) state.AvailableItems[id]=0;
                state.Money=unchecked(state.Money+(price/2)*Quantity);
            }
            QuantityActive=false;AdjustCursorAfterTransaction();return new(6);
        }
        if(input.Cancel) {QuantityActive=InsufficientFunds=false;return new(4);}
        if(input.HeldUp)
        {
            repeatCounter=unchecked(repeatCounter+1);if(repeatCounter<2)return new();repeatCounter=0;
            if(Quantity<available&&!InsufficientFunds) {Quantity++;return new(1);}
            return Buying?new():new(1);
        }
        if(input.HeldDown&&!InsufficientFunds)
        {
            repeatCounter=unchecked(repeatCounter+1);if(repeatCounter<2)return new();repeatCounter=0;
            if(Quantity>1) {Quantity--;return new(1);}
            return Buying?new():new(1);
        }
        return new();
    }
    private void CompactCurrentNative()
    {
        //42fae0 increments its scan index even after shifting, so adjacent empty rows can be skipped.
        for(int i=0;i<data.Counts[Category];i++)
        {
            bool remove=Category==0?data.Quantities[Category][i]<1:data.Quantities[Category][i]==0;
            if(!remove)continue;
            int count=data.Counts[Category];
            for(int j=i;j<count-1;j++){data.Ids[Category][j]=data.Ids[Category][j+1];data.Quantities[Category][j]=data.Quantities[Category][j+1];}
            data.Ids[Category][count-1]=-1;data.Quantities[Category][count-1]=0;data.Counts[Category]--;
        }
        if(Buying) data.CopyTo(persistent!);
    }
    private void AdjustCursorAfterTransaction()
    {
        if(Count<5) {if(Count<=Row)Row--;return;}
        if(Buying)
        {if(Count<=Row+Scroll || (Count>4&&Count<Scroll+5)){Scroll--;if(Scroll<0){Scroll=0;Row--;}}}
        else if(Row+Scroll>=Count) {Scroll--;if(Scroll<0){Scroll=0;Row--;}}
        else if(Count>4&&Count<Scroll+5) {Scroll--;if(Scroll<0){Scroll=0;Row--;}}
    }
    private int ItemInt(int id,int offset)
    {
        if((uint)id>=ItemCatalog.Count)throw new InvalidDataException("Shop item ID exceeds original catalog.");
        return items.GetItem(id).GetInteger((offset-30)/4);
    }
}


