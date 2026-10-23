using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40b4c0/40b610/40b400/40cfc0/40d030/40d070/40d410, fixed native50-slot overwrite ring.</summary>
public sealed class CombatParticleSystem(CombatAnimationAssets effectAssets)
{
    public const int Capacity=50;
    private readonly CombatParticleState[] particles=Enumerable.Range(0,Capacity).Select(_=>new CombatParticleState()).ToArray();
    public IReadOnlyList<CombatParticleState> Particles=>particles;
    public int NextSlot {get;private set;}
    public CombatParticleState ReserveRetained(){var value=particles[NextSlot];if(++NextSlot>=Capacity)NextSlot=0;return value;}
    public static CombatParticleSystem Load(string root)=>new(CombatAnimationAssets.Load(root,"EFFECT"u8));
    //4077f0 camera translation deliberately leaves particle depth unchanged.
    public void Translate(int dx,int dy)
    {
        foreach(var particle in particles)
        {
            int[] fields=particle.Fields;if(fields[6]==0)continue;
            fields[0]=unchecked(fields[0]+dx);fields[3]=unchecked(fields[3]+dx);
            fields[1]=unchecked(fields[1]+dy);fields[4]=unchecked(fields[4]+dy);
        }
    }

    public CombatParticleState SpawnProjectile(CombatActorImage owner,CombatAnimationAssets actualMagic,int x,int y,int depth,
        int facing,uint now,Action<int> playSound,Action<uint> applyScreenEffect)
    {
        if(actualMagic.Animation.Sequences.Length<1)throw new InvalidDataException("Magic requires actual first sequence.");
        //40c920 deliberately does not zero the overwritten pool slot.
        var particle=ReserveRetained();
        particle.Assets=particle.ContainerAssets=actualMagic;particle.SequenceIndex=0;particle.Owner=owner;
        int[] fields=particle.Fields;fields[17]=11;fields[5]=0;fields[6]=facing;
        var first=particle.FirstFrame;playSound(first.Words[7]);applyScreenEffect(first.FinalDword);
        fields[8]=first.Words[10];fields[9]=first.Words[15];fields[12]=first.Words[5];fields[13]=first.Words[3];fields[14]=first.Words[12];
        fields[15]=first.Words[24]==0?20:first.Words[24];fields[18]=0;
        fields[0]=fields[3]=x;fields[1]=fields[4]=y;fields[2]=depth;
        fields[16]=first.Words[25];fields[10]=unchecked(first.Words[6]*facing);fields[11]=first.Words[4];
        particle.AdvanceFrame(now,playSound);return particle;
    }

    public CombatParticleState? SpawnActor(CombatActorImage actor,int id,uint now,int visualDetail,Action<int> playSound)
    {
        if(visualDetail>=3)return null;
        var sprite=(actor.Animation??throw new InvalidOperationException("Actor particle requires bound current sprite.")).CurrentSprite;
        byte[] data=actor.Fields;
        return Spawn(unchecked(sprite.Metadata2+I(data,0x7c)),unchecked(I(data,0x78)-5),unchecked(I(data,0x78)+1),
            id,unchecked((sbyte)data[0xfa]),2,now,playSound);
    }
    public CombatParticleState? SpawnPositional(CombatHitParticle value,uint now,int visualDetail,Action<int> playSound)
        =>visualDetail>=3?null:Spawn(value.X,value.Y,value.Depth,value.Id,value.Facing,1,now,playSound);
    private CombatParticleState Spawn(int x,int y,int depth,int id,int facing,int category,uint now,Action<int> playSound)
    {
        int sequence=id-1;
        if((uint)sequence>=(uint)effectAssets.Animation.Sequences.Length)throw new InvalidDataException("Effect ID is outside native1-based ANI sequences.");
        var particle=ReserveRetained();particle.Clear();
        particle.Assets=particle.ContainerAssets=effectAssets;particle.SequenceIndex=sequence;int[] fields=particle.Fields;
        fields[0]=x;fields[1]=y;fields[2]=depth;fields[6]=facing;
        playSound(particle.FirstFrame.Words[7]);fields[5]=0;fields[8]=25;fields[16]=5;fields[17]=category;
        particle.AdvanceFrame(now,playSound);return particle;
    }
    // Call once at native40d410 logical render stage, after actors enter the common depth queue.
    // Draw packets intentionally capture PRE-update frame/position. Callbacks run synchronously in native slot order.
    public CombatParticlePass Advance(uint now,Action<int> playSound,
        Action<CombatParticleState,int> transformAtExpiry,Action<CombatParticleState> collideWithActors)
    {
        List<CombatParticleDraw> draws=[];bool active=false;
        for(int slot=0;slot<Capacity;slot++)
        {
            var particle=particles[slot];int[] fields=particle.Fields;if(fields[6]==0)continue;active=true;
            if(fields[6]==999){draws.Add(new(slot,0,0,0,999,null));fields[6]=0;continue;}
            if(!KeepAlive(particle,transformAtExpiry))continue;
            int code=fields[17]==0?unchecked(fields[6]<<3):unchecked(fields[6]*(fields[17]==11?11:10));
            draws.Add(new(slot,fields[3],fields[4],fields[2],code,particle.CurrentSprite));
            AdvanceMotion(particle,now,playSound);
            if(unchecked((short)particle.FirstFrame.Words[8])!=0)collideWithActors(particle);
        }
        return new(active,draws.ToArray());
    }
    private static bool KeepAlive(CombatParticleState particle,Action<CombatParticleState,int> transform)
    {
        int[] fields=particle.Fields;
        bool expire=fields[16] switch
        {
            0 or 4=>fields[0]<-20||fields[0]>660,
            1=>fields[2]<=unchecked(particle.CurrentSprite.Height-1+fields[4]),
            5=>fields[18]!=0,_=>false
        };
        if(!expire)return true;
        Expire(particle,transform);return false;
    }
    public static void Expire(CombatParticleState particle,Action<CombatParticleState,int> transform)
    {int effect=particle.FirstFrame.Words[22];if(effect!=0)transform(particle,effect);else particle.Clear();}
    private static void AdvanceMotion(CombatParticleState particle,uint now,Action<int> playSound)
    {
        int[] fields=particle.Fields;if(fields[7]>unchecked((int)now))return;
        unchecked
        {
            fields[0]+=fields[10];int acceleration=fields[12],speed=fields[11];
            if(acceleration<1){fields[1]+=speed;fields[11]=speed-acceleration;}
            else
            {
                fields[1]-=speed;fields[11]=speed-acceleration;
                if(fields[11]<1){fields[11]=1;fields[12]=-acceleration;particle.AdvanceFrame(now,playSound);return;}
            }
        }
        particle.AdvanceFrame(now,playSound);
    }
    private static int I(byte[] data,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset,4));
}
