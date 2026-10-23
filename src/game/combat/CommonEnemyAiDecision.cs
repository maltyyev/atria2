using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>Exact40f5f0..411710 common enemy decisions, shared13/25/26 entry and matching40e1a0 continuation.</summary>
public static class CommonEnemyAiDecision
{
    public static bool Supports(int type)=>type is >=10 and <=45;
    public static void Begin(byte[] actor,int type,byte[]? target,CombatAnimationCursor cursor,int minimumY,int maximumY,uint milliseconds,Func<int> next)
    {
        if(!Supports(type)) throw new ArgumentOutOfRangeException(nameof(type));
        int roll=Next(next),decision=roll%(type==10?11:type==24 || type is >=27 and <=33 or >=35 and <=39 or 44 or 45?7:9);
        if(type==36)
        {
            BanditAiDecision.PrepareMovement(actor,target,cursor,minimumY,maximumY,milliseconds,next,350,2,50,20);
            if(I(actor,0xb1)!=2)
            {
                BanditAiDecision.PrepareMovement(actor,target,cursor,minimumY,maximumY,milliseconds,next,40,2,50,20);
                if(I(actor,0xb1)==2)
                {
                    if((Next(next)&7)==0) { CombatJumpRuntime.Begin(actor,cursor.Info,cursor,milliseconds);return; }
                    BanditAiDecision.PrepareMovement(actor,target,cursor,minimumY,maximumY,milliseconds,next,10000,2,0,0);
                }
            }
        }
        else if(type is 34 or >=40 and <=43) PrepareKeepDistance(actor,target,cursor,minimumY,maximumY,milliseconds,next,200,2);
        else if(type==10) BanditAiDecision.PrepareMovement(actor,target,cursor,minimumY,maximumY,milliseconds,next,200,2,40,10);
        else if(type is 14 or 16 or 20 or 24 or >=27) BanditAiDecision.PrepareMovement(actor,target,cursor,minimumY,maximumY,milliseconds,next,type==14?100000:type is 30 or 31?150:type is 37 or 45?5000:250,2,type==24?30:20,10);
        else if(type==15) PrepareKeepDistance(actor,target,cursor,minimumY,maximumY,milliseconds,next,200,2);
        else PrepareSimple(actor,target,cursor,milliseconds,type switch {11=>180,17=>350,18 or 19=>10000,21 or 22 or 23=>5000,_=>100000},2);
        int mode=I(actor,0xb1);
        W(actor,0x32d,type==10?(mode==2?2:decision&1):type is 11 or 12?decision&1:type==15 || type is >=28 and <=34 or >=36?1:type is 17 or 18 or 19 or 20?0:2);
        switch(decision)
        {
            case 0:case 1:
                int addition=type switch {11=>Next(next)%60,14=>Next(next)%100,18 or 20=>Next(next)%150,24=>Next(next)%120,21 or 22 or 23=>70,27 or 31=>Next(next)%120,28 or 30 or 32=>Next(next)%100,29=>Next(next)%80,33=>100,34 or 42 or 43=>10,35=>Next(next)%150,36=>Next(next)%120,37=>120,38=>150,39=>Next(next)%90,40=>Next(next)%50,41=>50,44=>Next(next)%80,45=>Next(next)%40,_=>60};
                WS(actor,0xde,unchecked((short)(S(actor,0xde)+addition)));break;
            case 2:case 3:if(mode!=2) WS(actor,0xde,unchecked((short)(S(actor,0xde)>>1)));break;
            case 4:if(mode!=2) W(actor,0x9c,unchecked(-I(actor,0x9c)));break;
            case 5:case 6:
                if(type is 24 or 36) { CombatJumpRuntime.Begin(actor,cursor.Info,cursor,milliseconds);break; }
                cursor.BeginAutomaticIdle(0);return;
            default:cursor.BeginAutomaticIdle(0);return;
        }
        if(S(actor,0xde)<0) WS(actor,0xde,0);
        if(type is 16 or 20 or 21 or 22 or 23 or >=27 and <=33 or >=35 and <=39 or 44 or 45)
        {
            switch(Next(next)%6)
            {
                case 0:case 1:WS(actor,0xe0,unchecked((short)(S(actor,0xe0)+(type is 16 or 20 or 27?50:type==28?20:type>=29?15:25))));break;
                case 2:case 3:WS(actor,0xe0,unchecked((short)(S(actor,0xe0)>>1)));break;
                case 4:W(actor,0xa0,unchecked(-I(actor,0xa0)));break;
                case 5:WS(actor,0xe0,unchecked((short)(S(actor,0xe0)<<1)));break;
            }
            if(S(actor,0xe0)<0) WS(actor,0xe0,0);
        }
    }
    public static void Continue(byte[] actor,int type,byte[]? target,CombatAnimationCursor cursor,CombatInfoCharacter info,uint milliseconds,Func<int> next,short enemyAndPendingCount=0)
    {
        if(!Supports(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if(BanditAiDecision.AdvanceMovement(actor,target,cursor,milliseconds)) return;
        switch(type)
        {
            case 10:if(I(actor,0xb1)!=2) CombatAiAttack.Try(actor,target,cursor,info,milliseconds,50,100,0);return;
            case 11:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,0,50,0);return;
            case 12:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,20,50,Next(next)%3);return;
            case 13:case 25:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,0,20,0);return;
            case 26:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,10,40,0);return;
            case 14:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,50,2) &&
                   !CombatAiAttack.Try(actor,target,cursor,info,milliseconds,50,90,1))
                    CombatAiAttack.Try(actor,target,cursor,info,milliseconds,70,120,0);
                return;
            case 15:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,150,300,0);return;
            case 16:
                Next(next);
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,20,40,0)) CombatAiAttack.Try(actor,target,cursor,info,milliseconds,150,300,2);
                return;
            case 17:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,0,20,0)) CombatAiAttack.Try(actor,target,cursor,info,milliseconds,150,300,1);
                return;
            case 18:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,70,190,0) && I(actor,0x170)<30 && (Next(next)&15)==0)
                    CombatAiAttack.Try(actor,target,cursor,info,milliseconds,200,230,1);
                return;
            case 19:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,1,20,0);return;
            case 20:
                Next(next);
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,60,0)) CombatAiAttack.Try(actor,target,cursor,info,milliseconds,150,300,2);
                return;
            case 21:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,20,30,0) && !CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,60,1))
                    CombatAiAttack.Try(actor,target,cursor,info,milliseconds,150,300,2);
                return;
            case 22:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,20,40,0) && !CombatAiAttack.Try(actor,target,cursor,info,milliseconds,10,20,1))
                    CombatAiAttack.Try(actor,target,cursor,info,milliseconds,150,300,2);
                goto case 23;
            case 23:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,40,60,0)) CombatAiAttack.Try(actor,target,cursor,info,milliseconds,10,20,1);
                return;
            case 24:case 32:case 37:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,40,70,0);return;
            case 27:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,50,Next(next)%3) && I(actor,0x1ec)>1)
                    CombatAiAttack.Try(actor,target,cursor,info,milliseconds,80,110,3);
                return;
            case 28:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,50,0) && I(actor,0x1ec)>1)
                    CombatAiAttack.Try(actor,target,cursor,info,milliseconds,50,60,2);
                return;
            case 29:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,50,Next(next)%2))
                    CombatAiAttack.Try(actor,target,cursor,info,milliseconds,140,190,2);
                return;
            case 30:case 31:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,50,0,normalAttack:true);return;
            case 33:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,50,0) && !CombatAiAttack.Try(actor,target,cursor,info,milliseconds,70,80,1))
                    CombatAiAttack.Try(actor,target,cursor,info,milliseconds,80,110,2);
                return;
            case 34:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,160,300,0);return;
            case 35:if(I(actor,0xb1)!=2) CombatAiAttack.Try(actor,target,cursor,info,milliseconds,60,100,0);return;
            case 36:CombatAiAttack.Try(actor,target,cursor,info,milliseconds,60,70,0);return;
            case >=38 and <=44:ContinueBoss(actor,type,target,cursor,info,milliseconds,next,enemyAndPendingCount);return;
            case 45:
                if(!CombatAiAttack.Try(actor,target,cursor,info,milliseconds,30,60,0)) CombatAiAttack.Try(actor,target,cursor,info,milliseconds,70,120,1);
                return;

        }
    }
    //40e1a0 bosses retain independent attack-cycle count, index and strict cooldown.
    private static void ContinueBoss(byte[] a,int type,byte[]? target,CombatAnimationCursor c,CombatInfoCharacter info,uint time,Func<int> next,short enemyAndPending)
    {
        int now=unchecked((int)time);bool ready=I(a,0x355)!=0 && I(a,0x359)<now;
        bool Attack(int min,int max,int index,bool ignoreDepth=false)=>CombatAiAttack.Try(a,target,c,info,time,min,max,index,ignoreDepth:ignoreDepth);
        void Consume(bool increment=true)
        {
            W(a,0x355,unchecked(I(a,0x355)-1));if(increment) W(a,0x351,unchecked(I(a,0x351)+1));
            W(a,0x359,unchecked(I(a,0x35d)+now));
        }
        void Wrap(int last) { if(I(a,0x351)>last) W(a,0x351,0); }
        switch(type)
        {
            case 38:
                if(ready)
                {
                    if(Attack(100,200,0)) { Wrap(2);return; }
                    if(Attack(200,500,I(a,0x351),true)) { Consume();Wrap(2);return; }
                }
                Attack(0,150,Next(next)%2+3);Wrap(2);return;
            case 39:
                if(ready && Attack(200,400,I(a,0x351),true)) { Consume();return; }
                if(!Attack(0,10,Next(next)%2+1) && !Attack(60,120,3)) Attack(120,180,4);
                return;
            case 40:
                if(ready)
                {
                    if(enemyAndPending<3 && Attack(100,550,3)) return;
                    if(Attack(150,650,I(a,0x351),true)) { Consume();Wrap(2);return; }
                }
                Attack(30,90,4);Wrap(2);return;
            case 41:
                if(ready && Attack(200,500,I(a,0x351),true)) Consume();
                else Attack(70,170,3);
                Wrap(2);return;
            case 42:
                if(ready && Attack(200,400,I(a,0x351),true)) Consume();
                else if(!Attack(180,250,3)) Attack(100,170,2);
                Wrap(1);return;
            case 43:
                if(ready)
                {
                    if(enemyAndPending<4 && Attack(100,550,0)) return;
                    if(Attack(200,450,I(a,0x351),true)) { Consume();Wrap(4);return; }
                }
                Attack(70,170,5);Wrap(4);return;
            case 44:
                if(ready && Attack(200,300,2,true)) Consume(false);
                else Attack(40,130,Next(next)%2+1);
                Wrap(0);return;
        }
    }
    //40d920: direct target displacement; no close-range avoidance or random sample.
    private static void PrepareSimple(byte[] actor,byte[]? target,CombatAnimationCursor cursor,uint milliseconds,int threshold,int farMode)
    {
        W(actor,0xb1,1);W(actor,0xad,0);WS(actor,0xd5,0);
        if(target is null) { cursor.BeginAutomaticIdle(0);return; }
        int dx=unchecked(I(target,0x98)-I(actor,0x98)),dy=unchecked(I(target,0x78)-I(actor,0x78)),mode=1;
        W(actor,0xad,1);
        if(unchecked(Abs(dx)+Abs(dy))>threshold) { mode=farMode;BanditAiDecision.Turn(actor,dx<0?-1:1,cursor.CurrentSprite.Width); }
        cursor.BeginAutomaticMovement(mode,dx,dy,milliseconds);
    }
    //40dac0: far target means scaled walking; near target chooses a distant side point with two samples.
    private static void PrepareKeepDistance(byte[] actor,byte[]? target,CombatAnimationCursor cursor,int minimumY,int maximumY,uint milliseconds,Func<int> next,int threshold,int farMode)
    {
        W(actor,0xb1,1);W(actor,0xad,0);WS(actor,0xd5,0);
        if(target is null) { cursor.BeginAutomaticIdle(0);return; }
        int dx=unchecked(I(target,0x98)-I(actor,0x98)),dy=unchecked(I(target,0x78)-I(actor,0x78));W(actor,0xad,1);
        if(unchecked(Abs(dx)+Abs(dy))>=threshold) { cursor.BeginAutomaticMovement(1,dx>>3,dy>>1,milliseconds);return; }
        int x=Next(next)%100+(I(target,0x98)<321?500:50);
        dx=unchecked(x-I(actor,0x98));int range=unchecked(maximumY-minimumY);
        if(range==0) throw new InvalidDataException("Enemy avoidance vertical RNG divisor is zero.");
        dy=unchecked(Next(next)%range-I(actor,0x78)+minimumY);
        BanditAiDecision.Turn(actor,dx<0?-1:1,cursor.CurrentSprite.Width);cursor.BeginAutomaticMovement(farMode,dx,dy,milliseconds);
    }
    private static int Next(Func<int> next) { int value=next();if((uint)value>32767) throw new InvalidDataException("Enemy AI requires native15-bit RNG.");return value; }
    private static int Abs(int value) { int sign=value>>31;return unchecked((value^sign)-sign); }
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static short S(byte[] actor,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(offset,2));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
    private static void WS(byte[] actor,int offset,short value)=>BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(offset,2),value);
}


