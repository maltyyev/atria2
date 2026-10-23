using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40b930/40caf0 exact magic/status branches over the shared native particle pool.</summary>
public sealed class CombatParticleMagicRuntime
{
    private readonly string root;
    private readonly CombatSceneRuntime runtime;
    private readonly CombatParticleSystem pool;
    private readonly Func<uint> now;
    private readonly Func<int> random;
    private readonly Action<int> sound;
    private readonly Action<uint> screenEffect;
    private readonly CombatHitExecutor hits;
    public byte[] GlobalStatus {get;}=new byte[240];
    public CombatAnimationAssets? ActiveMagic {get;private set;}
    public int GlobalDepth {get;set;} //45c39c, supplied from actual battle camera context.
    public CombatParticleMagicRuntime(string root,CombatSceneRuntime runtime,CombatParticleSystem pool,CombatHitExecutor hits,
        Func<uint> now,Func<int> nextNativeRandom,Action<int> sound,Action<uint> screenEffect)
    {this.root=root;this.runtime=runtime;this.pool=pool;this.hits=hits;this.now=now;random=nextNativeRandom;this.sound=sound;this.screenEffect=screenEffect;}

    public void ApplyFrameEffect(int ownerIndex,int id,int argument)=>Apply(runtime.Actors[ownerIndex],null,id,argument,false);
    public void Transform(CombatParticleState value,int sequence)=>Apply(value.Owner??throw new InvalidOperationException("Particle transform has no owner."),value,sequence,10,true);
    public void ReleaseActiveMagic()=>ActiveMagic=null; //40a820 only after40d410 reports no active entries.
    public void Apply(CombatActorImage owner,CombatParticleState? existing,int id,int argument,bool transformed)
    {
        if(id is 148 or 149 or 152 or 154){Dispatch(owner,id);return;}
        if(id is 165 or 166){sound(id==165?340:341);CombatParticleMagic.Resurrect(runtime.Actors,owner,id==165?1:0);return;}
        var particle=existing??pool.ReserveRetained();int[] p=particle.Fields;
        if(existing is not null){p[0]=p[3];p[1]=p[4];}
        if(!transformed)
        {
            if(id==999){p[6]=999;return;}
            if(id<101){p[17]=argument;particle.ContainerAssets=owner.Binding?.Assets??throw new InvalidOperationException("Frame effect owner has no actual ANI.");}
            else {p[17]=11;ActiveMagic=CombatParticleMagic.Load(root,id);particle.ContainerAssets=ActiveMagic;}
        }
        else if(p[6]==-1){p[0]=unchecked(p[0]-particle.CurrentSprite.Width);p[3]=p[0];}
        if(Dispatch(owner,id))return;
        particle.Owner=owner;if(!transformed)p[6]=unchecked((sbyte)owner.Fields[0xfa]);
        particle.Assets=particle.ContainerAssets??throw new InvalidOperationException("Particle transform lost ANI owner.");
        particle.SequenceIndex=p[17]<11||transformed?id:0;
        if((uint)particle.SequenceIndex>=(uint)particle.Assets.Animation.Sequences.Length)throw new InvalidDataException("Frame effect sequence exceeds actual ANI.");
        p[5]=0;var first=particle.FirstFrame;sound(first.Words[7]);screenEffect(first.FinalDword);
        p[8]=first.Words[10];p[9]=first.Words[15];p[12]=first.Words[5];p[13]=first.Words[3];p[14]=first.Words[12];
        p[15]=first.Words[24]==0?20:first.Words[24];p[18]=0;
        if(existing is null)
        {
            var sprite=owner.Animation!.CurrentSprite;byte[] a=owner.Fields;int facing=unchecked((sbyte)a[0xfa]);
            if(p[17] is 0 or 1 or 2 or 3 or 4 or 11)
            {
                if(sprite.Points.Length<1)throw new InvalidDataException("Frame effect requires native sprite anchor point.");
                int anchor=sprite.Points[0].X;
                if(p[17]==4)p[6]=-p[6];
                p[0]=unchecked(I(a,0x7c)+(facing==1?anchor:p[17] is 2 or 3?anchor-sprite.Width:sprite.Width-anchor));
                p[1]=unchecked(sprite.Points[0].Y+I(a,0x80));p[2]=unchecked(I(a,0x78)+(p[17] is 2 or 3?-10:p[17]==11?1:-4));
            }
            else if(p[17]==5)
            {
                int ownerIndex=Index(owner),targetIndex=runtime.TargetIndices[ownerIndex];
                if(targetIndex<0){p[6]=0;return;}var target=runtime.Actors[targetIndex];
                p[0]=unchecked(target.Animation!.CurrentSprite.Metadata2+I(target.Fields,0x7c));p[1]=unchecked(I(target.Fields,0x78)-particle.CurrentSprite.Height);p[2]=I(target.Fields,0x78);
            }
        }
        p[16]=first.Words[25];p[10]=unchecked(first.Words[6]*p[6]);p[11]=first.Words[4];particle.AdvanceFrame(now(),sound);
    }

    public static int Relation(CombatActorImage owner,CombatActorImage target)
    {
        byte[] a=owner.Fields,t=target.Fields;if(I(t,0x60)!=0||I(t,0x170)<1||I(t,0x224)==0)return 2;
        sbyte team=unchecked((sbyte)a[0xa6]),other=unchecked((sbyte)t[0xa6]);
        if(team>=100)return other<100?1:0;
        return team!=other&&t[0xa7]!=0?1:0;
    }
    private void Status(CombatActorImage owner,int code,int bank,int field3)
    {
        int deadline=unchecked((I(owner.Fields,0x1ec)+5)*4000+(int)now());
        CombatStatusSlots.Insert(bank==0?owner.Fields.AsSpan(0x244,72):GlobalStatus,code,bank,field3,deadline,owner.Binding!.Info.Type,unchecked((int)now()));
    }
    private CombatParticleState Spawn(CombatActorImage owner,int x,int y,int depth,int facing=1)
        =>pool.SpawnProjectile(owner,ActiveMagic??throw new InvalidOperationException("Magic dispatch requires loaded ANI."),x,y,depth,facing,now(),sound,screenEffect);
    private void Launch(CombatActorImage owner,CombatActorImage target)=>hits.LaunchReaction(Index(owner),Index(target),new(10,10,6,3,unchecked((sbyte)owner.Fields[0xfa])),now());
    private int Index(CombatActorImage value){for(int i=0;i<runtime.Actors.Count;i++)if(ReferenceEquals(runtime.Actors[i],value))return i;throw new InvalidDataException("Magic actor outside current battle.");}

    public bool Dispatch(CombatActorImage owner,int id)
    {
        byte[] a=owner.Fields;int level=I(a,0x1ec),deadline=unchecked((level+5)*4000+(int)now());
        void Add(byte[] fields,int offset,int value)=>W(fields,offset,unchecked(I(fields,offset)+value));
        bool Roll(CombatActorImage target)=>I(target.Fields,0x17c)<random()%11;
        void Affect(int relation,Action<CombatActorImage> apply){foreach(var target in runtime.Actors)if(Relation(owner,target)==relation)apply(target);}
        switch(id)
        {
            case 106:for(int i=0;i<unchecked(level+2);i++)Spawn(owner,unchecked(owner.Animation!.CurrentSprite.Metadata2+I(a,0x7c)),unchecked(I(a,0x78)-30),unchecked(I(a,0x78)+1),unchecked((sbyte)a[0xfa]));return true;
            case 107:Status(owner,7,0,0);Add(a,0x29c,40);return false;
            case 108:Status(owner,8,0,0);Add(a,0x2a0,5);return false;
            case 111:Status(owner,11,0,0);W(a,0x2b0,1);return false;
            case 112:Status(owner,12,0,0);W(a,0x240,unchecked((level+10)*30));Add(a,0x2a4,6);Add(a,0x29c,6);return false;
            case 114:
                int x=0;for(int i=0;i<unchecked(level+3);i++){x+=30;if(x>500)x=20;int depth=unchecked(random()%100+GlobalDepth),y=-(random()%100);var p=Spawn(owner,x,y,depth);if(p.Fields[10]<0)p.Fields[10]=-p.Fields[10];}return true;
            case 116:Status(owner,16,1,1);Affect(1,t=>{if(Roll(t))W(t.Fields,0x2ac,deadline);});break;
            case 117:Status(owner,17,1,0);Affect(0,t=>Add(t.Fields,0x2a4,4));break;
            case 119:
                foreach(var actor in runtime.Actors)if(actor.Binding is not null)actor.Fields.AsSpan(0x23c,137).Clear();Array.Clear(GlobalStatus);Spawn(owner,320,180,400);return true;
            case 121:{var p=Spawn(owner,0,0,350);if(p.Fields[10]<0)p.Fields[10]=-p.Fields[10];p.Fields[6]=1;return true;}
            case 126:Status(owner,26,1,1);Affect(1,t=>{if(Roll(t))Add(t.Fields,0x2a4,-4);});Spawn(owner,250,180,400);return true;
            case 127:Status(owner,27,1,1);Affect(1,t=>Add(t.Fields,0x2a0,-3));Spawn(owner,250,180,400);return true;
            case 128:Status(owner,28,0,0);W(a,0x2c0,1);W(a,0x23c,deadline);W(a,0x240,deadline);Spawn(owner,300,180,400).Fields[6]=1;return true;
            case 130:Status(owner,30,1,0);Affect(0,t=>{W(t.Fields,0x2bc,1);Add(t.Fields,0x2a0,3);});Spawn(owner,300,180,400);return true;
            case 131:Status(owner,31,1,1);Affect(1,t=>{W(t.Fields,0x2b4,1);Add(t.Fields,0x2a0,-3);Add(t.Fields,0x2a4,-3);});break;
            case 132:Status(owner,32,1,1);Affect(1,t=>{if(Roll(t))W(t.Fields,0x2b4,1);});Spawn(owner,320,180,400);return true;
            case 135:Status(owner,35,1,0);Affect(0,t=>{Add(t.Fields,0x2a0,1);Add(t.Fields,0x2a4,2);Add(t.Fields,0x29c,10);Add(t.Fields,0x2a8,2);Spawn(owner,I(t.Fields,0x7c),I(t.Fields,0x78),unchecked(I(t.Fields,0x78)+1));});return true;
            case 139:Spawn(owner,280,150,400);CombatParticleMagic.Resurrect(runtime.Actors,owner,0);return true;
            case 140:Status(owner,40,1,0);Affect(0,t=>{Add(t.Fields,0x2a4,3);Add(t.Fields,0x2b8,5);});Spawn(owner,300,180,400);return true;
            case 141:
                foreach(var t in runtime.Actors)if(Relation(owner,t)!=2&&Roll(t)){W(t.Fields,0x170,Math.Max(0,unchecked(I(t.Fields,0x170)+(-5-I(a,0x17c))*20)));Launch(owner,t);}Spawn(owner,280,180,400);return true;
            case 143:
                for(int i=0;i<10;i++)if(I(GlobalStatus,i*24+4)==32){W(GlobalStatus,i*24,0);break;}
                Affect(0,t=>{W(t.Fields,0x2b4,0);Spawn(owner,I(t.Fields,0x7c),unchecked(t.Animation!.CurrentSprite.Height-40+I(t.Fields,0x80)),unchecked(I(t.Fields,0x78)+1));});return true;
            case 144:
                Affect(1,t=>{if(t.Binding!.Info.Type is 12 or 21 or 22 or 23 or 37 or 41){Launch(owner,t);W(t.Fields,0x170,Math.Max(0,unchecked(I(t.Fields,0x170)+(-5-level)*10)));}});Spawn(owner,320,180,400);return true;
            case 145:Affect(0,t=>{Spawn(owner,I(t.Fields,0x7c),unchecked(I(t.Fields,0x78)-10),unchecked(I(t.Fields,0x78)+1));W(t.Fields,0x170,Math.Min(I(t.Fields,0x16c),unchecked(I(t.Fields,0x170)+(level+2)*10)));});return true;
            case 146:Affect(1,t=>{Launch(owner,t);W(t.Fields,0xc1,CombatActorMaintenance.ModifyActionInterval(t.Fields,100));W(t.Fields,0x170,Math.Max(0,unchecked(I(t.Fields,0x170)+(-20-level)*10)));});Spawn(owner,0,0,400);return true;
            case 148:W(a,0x170,Math.Min(I(a,0x16c),unchecked(I(a,0x170)+level*30)));return false;
            case 149:Status(owner,49,0,0);Add(a,0x2a8,(level>5?1:0)+2);return false;
            case 152:Status(owner,52,0,0);Add(a,0x2a4,5);return false;
            case 154:Status(owner,54,0,0);Add(a,0x2a4,I(a,0x1f0));Add(a,0x2a0,-I(a,0x1f0));return false;
            case 156:Status(owner,30,1,0);Affect(0,t=>Add(t.Fields,0x2a0,3));Spawn(owner,300,180,400);return true;
            case 157:Affect(0,t=>{W(t.Fields,0x170,I(t.Fields,0x16c));Spawn(owner,I(t.Fields,0x7c),unchecked(t.Animation!.CurrentSprite.Height-70+I(t.Fields,0x80)),unchecked(I(t.Fields,0x78)+1));});return true;
            default:return false; //Native explicitdefault0; caller continues generic animation setup.
        }
        Spawn(owner,280,180,400);return true;
    }
    private static int I(byte[] bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));
    private static void W(byte[] bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset,4),value);
}
