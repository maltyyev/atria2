using System.Globalization;
using Atria.Game.Exploration;

namespace Atria.Game.Combat;

/// <summary>Raw signed native globals71..75; global70 selects this variant before resolution.</summary>
public readonly record struct CombatRandomEncounterInputs(short Region,short Category,
    short FieldCount,short ForestCount,short MountainCount,int RequestedNumber)
{
    public static CombatRandomEncounterInputs FromGlobals(ReadOnlySpan<short> globals,int requestedNumber)
    {
        if(globals.Length<=75) throw new ArgumentException("Encounter globals70..75 are required.",nameof(globals));
        if(globals[70]!=2) throw new InvalidOperationException("Random field battle requires native variant2.");
        return new(globals[71],globals[72],globals[73],globals[74],globals[75],requestedNumber);
    }
}

/// <summary>4085de..408640. Resolve after413930 seeds the shared CRT RNG, before4128c0 loads assets.</summary>
public readonly record struct CombatRandomEncounterResolution(int SceneRegion,int SceneNumber,bool UsedRandomFallback)
{
    public const int MusicTrack=21;
    public string ResourceName=>"R"+SceneRegion.ToString(CultureInfo.InvariantCulture)+"N"+SceneNumber.ToString("D2",CultureInfo.InvariantCulture);
    public string FileName=>ResourceName+".SOD";

    public static CombatRandomEncounterResolution Resolve(CombatRandomEncounterInputs input,LegacyRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        int number=input.Category switch
        {
            0=>input.FieldCount,
            1=>input.ForestCount,
            2=>input.MountainCount+15,
            _=>input.RequestedNumber //40860d preserves the incoming argument for any other category.
        };
        bool fallback=input.Region>=8 || number>=21; //Signed JG7/JG20; negative values are not clamped.
        int region=input.Region+1;
        if(fallback)
        {
            number=random.Next()%20+1; //40864c..59 pushes the second sprintf argument first.
            region=random.Next()%8+1;
        }
        _=random.Next(); //40863b executes for BOTH branches, including unsuccessful subsequent asset lookup.
        return new(region,number,fallback);
    }
}
