namespace Atria.Game.Settings;

/// <summary>The first three bytes of native save context 46dc24; no OS mixer state.</summary>
public sealed class NativeSettingsState
{
    public const byte SnapshotVersion=1;
    public byte Music { get; private set; }
    public byte Effects { get; private set; }
    public byte ThirdOption { get; private set; }
    public byte PlayerCount=>ThirdOption; //408570 reads46dc26 as signed player-count byte.
    public NativeSettingsState(byte music=2,byte effects=4,byte thirdOption=1)
    { Set(music,effects,thirdOption); }
    public void Set(byte music,byte effects,byte thirdOption)
    {
        if(music>4||effects>4||thirdOption is <1 or >2) throw new InvalidDataException("Invalid native audio/options domain.");
        Music=music;Effects=effects;ThirdOption=thirdOption;
    }
    public byte[] Encode()=>[SnapshotVersion,Music,Effects,ThirdOption];
    public static NativeSettingsState Parse(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length!=4||bytes[0]!=SnapshotVersion) throw new InvalidDataException("Unsupported settings snapshot.");
        return new(bytes[1],bytes[2],bytes[3]);
    }
}
