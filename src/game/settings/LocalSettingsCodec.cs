using System.Buffers.Binary;
using Atria.Resources;

namespace Atria.Game.Settings;

/// <summary>Modern app-local config. Physical scan identity and both eight-action blocks are retained.</summary>
public static class LocalSettingsCodec
{
    public const int EncodedSize=28;
    public const uint Version=1;
    public static byte[] Encode(NativeSettingsState settings,ControlBindings bindings)
    {
        byte[] bytes=new byte[EncodedSize];"AT2C"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),Version);
        settings.Encode().CopyTo(bytes,8);
        for(int player=0;player<2;player++) for(int action=0;action<8;action++) bytes[12+player*8+action]=bindings[player,action];
        return bytes;
    }
    public static (NativeSettingsState Settings,ControlBindings Bindings) Parse(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length!=EncodedSize||!bytes[..4].SequenceEqual("AT2C"u8)||BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..])!=Version)
            throw new InvalidDataException("Unsupported local settings format.");
        byte[] native=new byte[64];
        for(int i=0;i<16;i++) BinaryPrimitives.WriteUInt32LittleEndian(native.AsSpan(i*4),bytes[12+i]);
        return (NativeSettingsState.Parse(bytes.Slice(8,4)),ControlBindings.Parse(native));
    }
    // Authored modern remapping boundary; original mode250 has no remapping UI.
    public static ControlBindings Remap(ControlBindings bindings,int player,int action,byte physicalScan)
    {
        if((uint)player>=2||(uint)action>=8||physicalScan==0) throw new ArgumentOutOfRangeException();
        byte[] native=new byte[64];
        for(int p=0;p<2;p++) for(int a=0;a<8;a++)
            BinaryPrimitives.WriteUInt32LittleEndian(native.AsSpan((p*8+a)*4),p==player&&a==action?physicalScan:bindings[p,a]);
        return ControlBindings.Parse(native);
    }
}
