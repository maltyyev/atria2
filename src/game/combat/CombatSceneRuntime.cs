using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Atria.Game.Combat;

public sealed class CombatInfoBinding
{
    public int NativeIndex { get; }
    public byte[] MutableFields { get; }
    public CombatInfoCharacter Info { get; }
    public CombatAnimationAssets Assets { get; }
    internal CombatInfoBinding(int index,int animationIndex,CombatInfoCharacter source,CombatAnimationAssets assets)
    {
        NativeIndex=index; MutableFields=source.Fields.ToArray(); Assets=assets;
        BinaryPrimitives.WriteInt32LittleEndian(MutableFields.AsSpan(0xb30,4),animationIndex);
        Info=new(source.AnimationResource,MutableFields){AnimationOrdinal=source.AnimationOrdinal};
    }
}

public sealed class CombatActorImage
{
    public byte[] Fields { get; }=new byte[CombatSceneDefinition.RuntimeRecordSize];
    public CombatInfoBinding? Binding { get; internal set; }
    public CombatAnimationCursor? Animation { get; internal set; }
    public int ControlPlayer { get; internal set; }=-1;
    public int CurrentCommandIndex { get; internal set; }=-1; //native actor0 P-record pointer
}

/// <summary>4128c0/412fa0 actual pointer-free actor and shared INF ownership, without claiming battle completion.</summary>
public sealed class CombatSceneRuntime
{
    private readonly string root;
    private readonly Action<int>? loadNumericSound;
    private readonly List<CombatInfoBinding> bindings=[];
    private readonly List<CombatAnimationAssets> animations=[];
    private readonly CombatActorImage[] actors;
    private bool declaredBound,partyPopulated,attributesBound;
    public CombatSceneDefinition Scene { get; }
    public IReadOnlyList<CombatActorImage> Actors { get; }
    public IReadOnlyList<CombatInfoBinding> InfoBindings => bindings.AsReadOnly();
    public int[] TargetIndices { get; }
    public int InitialControlRank { get; }=3; //413930 default45acfc
    public int AccumulatedAward { get; private set; }
    public CombatSceneRuntime(string resourceRoot,CombatSceneDefinition scene,uint milliseconds,Action<int>? loadNumericSound=null)
    {
        root=resourceRoot; Scene=scene;this.loadNumericSound=loadNumericSound;
        actors=Enumerable.Range(0,scene.RuntimeActorCount).Select(_=>new CombatActorImage()).ToArray();
        Actors=Array.AsReadOnly(actors); TargetIndices=Enumerable.Repeat(-1,actors.Length).ToArray();
        byte[] typeCache=new byte[60]; int next=0,markerFacing=1;
        for(int i=0;i<scene.Source.Count;i++)
        {
            var source=scene.Source.GetObject(i); int type=source.GetInteger(2);
            if(type==0)
            {
                markerFacing=source.GetInteger(10); if(markerFacing is 0 or 1) markerFacing=1;
                for(int party=0;party<6;party++)
                {
                    byte[] record=actors[scene.PartyStartIndex+party].Fields;
                    W(record,0x70,source.GetInteger(0)); W(record,0x7c,source.GetInteger(0));
                    W(record,0x78,unchecked(scene.Header73+10+party*20));
                    record[0xfa]=unchecked((byte)markerFacing); record[0xa6]=100; record[0xa7]=1;
                }
                continue;
            }
            if((uint)type>=60) throw new InvalidDataException("Combat scene type exceeds native cache.");
            if(typeCache[type]==0) typeCache[type]=checked((byte)LoadInfo(Encoding.ASCII.GetBytes(type.ToString(CultureInfo.InvariantCulture)+".inf")).NativeIndex);
            CombatInfoBinding binding=bindings[typeCache[type]];
            CombatActorImage actor=actors[next++]; byte[] data=actor.Fields; actor.Binding=binding;
            actor.Animation=new(data,binding.Info,binding.Assets);
            for(int slot=0;slot<6;slot++) W(data,0x208+slot*4,-1);
            W(data,0x160,1); W(data,0x16c,source.GetInteger(6)); W(data,0x170,source.GetInteger(6));
            W(data,0x174,source.GetInteger(7)); W(data,0x178,source.GetInteger(7));
            W(data,0x14,source.GetInteger(6)); W(data,0x28,source.GetInteger(7));
            W(data,0x1f4,unchecked(source.GetInteger(8)+20)); W(data,0x1f0,source.GetInteger(9));
            W(data,0x1ec,1); W(data,0x17c,1); W(data,0x60,source.GetInteger(13)); W(data,0x64,source.GetInteger(14));
            W(data,0x6c,unchecked((int)milliseconds)); W(data,0x68,source.GetInteger(12)); W(data,0x331,binding.NativeIndex);
            W(data,0x70,source.GetInteger(0)); W(data,0x98,source.GetInteger(0));
            W(data,0x78,unchecked(source.GetInteger(1)+CombatSceneYOffsetTable.Values[type]));
            data[0xfa]=I(actors[scene.PartyStartIndex].Fields,0x70)<=source.GetInteger(0)?(byte)255:(byte)1;
            int team=source.GetInteger(15); if(team==0) team=1; data[0xa6]=unchecked((byte)team);
            if(unchecked((sbyte)data[0xa6])>99) data[0xfa]=unchecked((byte)markerFacing);
            data[0xa7]=source.GetInteger(10)<2?(byte)1:(byte)0;
            for(int name=0;name<3;name++)
            {
                ReadOnlySpan<byte> text=source.GetString(name).Span;
                if(text.Length>=20) throw new InvalidDataException("Combat scene string exceeds native20-byte actor destination.");
                text.CopyTo(data.AsSpan(0x2e9+name*20));
            }
        }
    }

    public void BindDeclaredAnimations(uint milliseconds)
    {
        if(declaredBound) throw new InvalidOperationException("Declared combat animations are already initialized.");
        for(int i=0;i<Scene.PartyStartIndex;i++) BindInitial(actors[i],milliseconds);
        declaredBound=true;
    }
    public void PopulateSelectedParty(CombatPartySelection selection)
    {
        if(!selection.Completed) throw new InvalidOperationException("Actual prebattle confirmation is required.");
        if(!declaredBound || partyPopulated) throw new InvalidOperationException("Combat party population is out of native setup order.");
        string[] names=["ROLAND","MIST","SWALLOW","BRAIUN","EMERALD","TIGER"];
        for(int party=0;party<6;party++)
        {
            ReadOnlySpan<byte> source=selection.Party.Span.Slice(party*280,280);
            if(I(source,256)==0) continue;
            CombatActorImage actor=actors[Scene.PartyStartIndex+party]; byte[] data=actor.Fields;
            source.CopyTo(data.AsSpan(0x124,280)); W(data,0x34,InitialControlRank); W(data,0x22c,InitialControlRank);
            W(data,0x14,I(data,0x16c)); W(data,0x28,I(data,0x174));
            if(I(data,0x170)<I(data,0x16c)) W(data,0x170,unchecked(I(data,0x170)+1));
            int form=I(source,224);
            CombatInfoBinding binding=LoadInfo(Encoding.ASCII.GetBytes(names[party]+form.ToString(CultureInfo.InvariantCulture)));
            actor.Binding=binding; actor.Animation=new(data,binding.Info,binding.Assets); W(data,0x331,binding.NativeIndex);
        }
        partyPopulated=true;
    }
    public void BindAllAttributes(int regionIndex,int variant,uint milliseconds,Func<int> nextNativeRandom)
    {
        if(!partyPopulated || attributesBound) throw new InvalidOperationException("Combat attributes must initialize once after actual party selection.");
        int award=AccumulatedAward;
        foreach(CombatActorImage actor in actors)
        {
            if(actor.Binding is not {} binding || I(actor.Fields,0x1ec)==0 || I(actor.Fields,0x224)==0) continue;
            CombatEnemyAttributes.Initialize(actor.Fields,binding.MutableFields,regionIndex,variant,nextNativeRandom,ref award);
            W(actor.Fields,0x331,0); BindInitial(actor,milliseconds);
        }
        AccumulatedAward=award;
        attributesBound=true;
    }
    public void BindPlayerRoles(CombatPartySelection selection)
    {
        if(!selection.Completed) throw new InvalidOperationException("Actual party leader selection is required.");
        if(!attributesBound) throw new InvalidOperationException("Bind actual player roles after party population and final attribute initialization.");
        for(int player=0;player<selection.PlayerCount;player++)
        {
            CombatActorImage actor=actors[Scene.PartyStartIndex+selection.Leaders[player]];
            actor.Fields[0xa4]=checked((byte)(player+1)); actor.ControlPlayer=player;
        }
        CombatActorImage leader=actors[Scene.PartyStartIndex+selection.Leaders[0]];
        CombatPartyControl.Flash(leader.Fields,0xffff); W(leader.Fields,0x230,1);
    }
    private static void BindInitial(CombatActorImage actor,uint milliseconds)
    {
        if(actor.Animation is null || I(actor.Fields,0x1ec)==0) return;
        W(actor.Fields,0x224,1); actor.Animation.BeginMotion(0,milliseconds); W(actor.Fields,0xcd,unchecked((int)milliseconds+50));
    }
    private CombatInfoBinding LoadInfo(ReadOnlySpan<byte> name)
    {
        CombatInfoDefinition file=CombatInfoDefinition.Load(root,name,loadNumericSound); int firstAnimation=animations.Count;
        foreach(ReadOnlyMemory<byte> resource in file.AnimationResources)
        {
            if(animations.Count>=19) throw new InvalidDataException("Combat exceeds native animation-slot limit.");
            animations.Add(CombatAnimationAssets.Load(root,resource.Span));
        }
        foreach(CombatInfoCharacter character in file.Characters)
        {
            if(bindings.Count>=20) throw new InvalidDataException("Combat exceeds native character-info storage.");
            int animationIndex=firstAnimation+character.AnimationOrdinal;
            bindings.Add(new(bindings.Count,animationIndex,character,animations[animationIndex]));
        }
        if(file.Characters.Count==0) throw new InvalidDataException("Combat info resource has no character definition.");
        return bindings[^1];
    }
    private static int I(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static void W(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..],value);
}
