using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

/// <summary>Recovered manual walking409070 and normal attack40b230; other action handlers remain separate.</summary>
public static class CombatManualControl
{
    public static void ExecuteCommand(CombatSceneRuntime runtime, CombatActorImage image, GameState state,
        uint milliseconds, bool globalEffectBusy, Action<int> playSound, Action<int> showRankNotice, Action<int> showConsumedItemNotice)
    {
        byte[] actor=image.Fields; var info=image.Binding!.Info;
        int category=I(actor,0x2c);
        if(category==0)
        {
            playSound(info.Read(0xae0)); int rank=I(actor,0x34); showRankNotice(rank);
            for(int i=runtime.Scene.PartyStartIndex;i<runtime.Actors.Count;i++)
            { W(runtime.Actors[i].Fields,0x22c,rank); W(runtime.Actors[i].Fields,0x34,rank); }
            return;
        }
        if(globalEffectBusy) return;
        int first=0,count=info.Read(0xb28);
        while(first<count && info.Read(0x7e4+first*24)!=category) first++;
        if(first==count) return;
        int index=unchecked(first+I(actor,0x34)+I(actor,0x30));
        if((uint)index>=(uint)count) throw new InvalidDataException("Selected combat P command exceeds actual INF records.");
        int at=0x7d8+index*24,cost=info.Read(at+4),mp=I(actor,0x178);
        if(mp<cost)
        {
            int item=64,slot=-1;
            if(unchecked(mp+100)>=cost) slot=EquipmentSlot(actor,64);
            if(slot<0)
            {
                item=65;
                if(unchecked(mp+200)<cost) return;
                slot=EquipmentSlot(actor,65); if(slot<0) return;
            }
            mp=unchecked(mp+(item==64?100:200)); W(actor,0x178,mp); W(actor,0x208+slot*4,-1);
            state.OwnedItems[item]=unchecked(state.OwnedItems[item]-1); showConsumedItemNotice(item==64?4:5);
        }
        mp=unchecked(mp-cost); W(actor,0x178,mp); W(actor,0x18,mp); image.CurrentCommandIndex=index;
        image.Animation!.BeginSequenceCommand(info.Read(at),28,milliseconds);
    }
    public static void ContinueAction(CombatActorImage image, ReadOnlySpan<byte> controls, CombatScanInput input, uint milliseconds)
    {
        byte[] actor=image.Fields; var info=image.Binding!.Info; var cursor=image.Animation!;
        if((I(actor,0xad)&0x20)==0) return;
        for(int index=info.Read(0xb1c);index<info.Read(0xb20);index++)
        {
            int at=0x150+index*0x2c;
            byte forward=unchecked((sbyte)actor[0xfa])==-1?controls[0]:controls[1];
            byte back=unchecked((sbyte)actor[0xfa])==-1?controls[1]:controls[0];
            if(I(actor,0xad)!=info.Read(at+4)) continue;
            if(!input.IsHeld(controls[5]))
            {
                if(input.IsHeld(controls[4]) && I(actor,0xb1)==0 && I(actor,0xb9) is >15 and <18)
                    cursor.BeginVariation(index,milliseconds);
                continue;
            }
            if(I(actor,0xb9)!=0 && I(actor,0xb1)!=0) continue;
            int count=0;
            for(int direction=0;direction<4;direction++) if(input.IsHeld(controls[direction])) count++;
            int command=info.Read(at+16); byte selected=0; bool match=false;
            if(count==1)
            {
                switch(command)
                {
                    case 2:selected=controls[3];match=true;break;
                    case 4:selected=back;match=true;break;
                    case 6:selected=forward;match=true;break;
                    case 8:selected=controls[2];match=true;break;
                }
            }
            else if(count==2)
            {
                switch(command)
                {
                    case 1:if(input.IsHeld(controls[3])) { selected=back;match=true; } break;
                    case 3:if(input.IsHeld(forward)) { selected=controls[3];match=true; } break;
                    case 7:if(input.IsHeld(back)) { selected=controls[2];match=true; } break;
                    case 9:if(input.IsHeld(controls[2])) { selected=forward;match=true; } break;
                }
            }
            if(match && input.IsHeld(selected))
            {
                CombatHitReaction.CancelOffsetMotion(image); cursor.BeginVariation(index,milliseconds); return;
            }
        }
    }
    public static void Attack(byte[] actor, CombatInfoCharacter info, CombatAnimationCursor cursor, uint milliseconds)
    {
        W(actor, 0x34d, 0);
        int type = info.Type;
        if (I(actor, 0xb1) == 2 && type != 38 && (type < 40 || type > 43))
        {
            int count = info.Read(0xb20);
            if (count > 0) cursor.BeginAttack(count - 1, milliseconds);
            return;
        }
        if (actor[0xa8] == 100 && unchecked((int)milliseconds) < I(actor, 0xcd))
            cursor.BeginAttack(BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(0xdb, 2)) + 1, milliseconds);
        else cursor.BeginAttack(0, milliseconds);
    }

    public static void Walk(byte[] actor, CombatInfoCharacter info, CombatAnimationCursor cursor,
        ReadOnlySpan<byte> controls, CombatScanInput input, uint milliseconds,bool? run=null)
    {
        if (actor.Length != CombatSceneDefinition.RuntimeRecordSize || controls.Length != 8)
            throw new ArgumentException("Incomplete combat actor or control binding.");
        int now = unchecked((int)milliseconds);
        bool idle = true; sbyte horizontal = 0, vertical = 0;
        while (true)
        {
            int mode = 0;
            if (!input.IsHeld(controls[4]))
            {
                if (input.IsHeld(controls[1]))
                {
                    idle = false; horizontal = 1;
                    mode = run is {} requestedRun ? (requestedRun?2:1) : I(actor, 0xb1) == 2 ? 2 : 1;
                    W(actor, 0x34d, mode == 2 ? 0 : controls[1]);
                }
                else if (input.IsHeld(controls[0]))
                {
                    idle = false; horizontal = -1;
                    mode = run is {} requestedRun ? (requestedRun?2:1) : I(actor, 0xb1) == 2 ? 2 : 1;
                    W(actor, 0x34d, mode == 2 ? 0 : controls[0]);
                }
                if (input.IsHeld(controls[2]))
                {
                    idle = false; vertical = -1; W(actor, 0x34d, 0);
                    mode = run is {} requestedRun ? (requestedRun?2:1) : I(actor, 0xb1) == 2 ? 2 : 1;
                }
                else if (input.IsHeld(controls[3]))
                {
                    idle = false; vertical = 1; W(actor, 0x34d, 0);
                    mode = run is {} requestedRun ? (requestedRun?2:1) : I(actor, 0xb1) == 2 ? 2 : 1;
                }
            }
            if (!idle)
            {
                W(actor, 0xb1, mode);
                if (unchecked((sbyte)actor[0xfa]) != horizontal && horizontal != 0)
                {
                    actor[0xfa] = unchecked((byte)horizontal);
                    W(actor, 0x70, unchecked(I(actor, 0x70) - cursor.CurrentSprite.Width * horizontal));
                    cursor.BeginMotion(4, milliseconds); return;
                }
                horizontal = unchecked((sbyte)(info.Read(mode * 0x18 + 4) * horizontal));
                vertical = unchecked((sbyte)(info.Read(mode * 0x18 + 8) * vertical));
            }
            else
            {
                mode = I(actor, 0xb1) == 2 ? 3 : 0; W(actor, 0xb1, mode);
                if (mode == 3) W(actor, 0xad, 0x40);
            }
            if (!cursor.IsMotionSequence(mode))
            {
                if (run is null && mode == 1 && I(actor, 0x345) != 0 && I(actor, 0x34d) == I(actor, 0x345) && I(actor, 0x349) > now)
                {
                    W(actor, 0xb1, 2); W(actor, 0x349, 0); W(actor, 0x345, 0);
                    // Native restarts input decisions with the already accumulated direction bytes.
                    continue;
                }
                if (mode != 1 || I(actor, 0x345) == 0)
                {
                    W(actor, 0x345, I(actor, 0x34d)); W(actor, 0x349, unchecked(now + 180));
                }
                else { W(actor, 0x345, 0); W(actor, 0x349, 0); }
                cursor.BindManualMotion(mode, milliseconds);
            }
            if (cursor.FrameIndex >= cursor.SequenceFrameCount)
                BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(0xd5, 2), 0);
            if (I(actor, 0xc9) < now && (cursor.CurrentFrame.FinalDword & 2) == 0)
            {
                W(actor, 0x70, unchecked(I(actor, 0x70) + horizontal));
                W(actor, 0x78, unchecked(I(actor, 0x78) + vertical));
                W(actor, 0xc9, unchecked(I(actor, 0xc5) + now));
            }
            return;
        }
    }
    private static int I(byte[] actor, int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
    private static void W(byte[] actor, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
    private static int EquipmentSlot(byte[] actor,int item)
    {
        for(int i=0;i<6;i++) if(I(actor,0x208+i*4)==item) return i;
        return -1;
    }
}
