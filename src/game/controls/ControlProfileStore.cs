using System.Text.Json;

namespace Atria.Game.Controls;

/// <summary>Explicit JSON schema, no reflection; independent of legacy save/config records.</summary>
public static class ControlProfileStore
{
    public static byte[] Encode(ControlProfile p)
    {
        using var stream=new MemoryStream();using(var w=new Utf8JsonWriter(stream,new(){Indented=true}))
        {
            w.WriteStartObject();w.WriteNumber("version",2);w.WriteNumber("deadZone",p.DeadZone);w.WriteNumber("trigger",p.TriggerThreshold);
            w.WriteBoolean("vibration",p.Vibration);w.WriteBoolean("hints",p.Hints);w.WriteNumber("autosaveLimit",p.AutosaveLimit);w.WriteStartArray("players");
            foreach(var player in p.Players)
            {
                w.WriteStartObject();w.WriteNumber("pad",player.PadSlot);w.WriteBoolean("keyboard",player.Keyboard);w.WriteBoolean("classic",player.Classic);w.WriteString("preset",player.Preset);
                w.WriteStartArray("bindings");foreach(var row in player.Bindings)WriteRow(w,row);w.WriteEndArray();
                w.WriteStartObject("direct");foreach(var pair in player.Direct){w.WritePropertyName(pair.Key);WriteRow(w,pair.Value);}w.WriteEndObject();
                w.WriteStartArray("quick");foreach(var row in player.Quick){w.WriteStartArray();foreach(string id in row)w.WriteStringValue(id);w.WriteEndArray();}w.WriteEndArray();w.WriteEndObject();
            }
            w.WriteEndArray();w.WriteEndObject();
        }
        return stream.ToArray();
    }
    private static void WriteRow(Utf8JsonWriter w,InputBinding[] row)
    {w.WriteStartArray();foreach(var b in row){w.WriteStartArray();w.WriteNumberValue((int)b.Kind);w.WriteNumberValue(b.Code);w.WriteEndArray();}w.WriteEndArray();}
    public static ControlProfile Decode(ReadOnlyMemory<byte> bytes)
    {
        if(bytes.Length>131072)throw new InvalidDataException("Control profile too large.");
        using var doc=JsonDocument.Parse(bytes);var r=doc.RootElement;
        if(r.GetProperty("version").GetInt32()!=2)throw new InvalidDataException("Unknown controls version.");
        var p=new ControlProfile {DeadZone=r.GetProperty("deadZone").GetInt32(),TriggerThreshold=r.GetProperty("trigger").GetInt32(),Vibration=r.GetProperty("vibration").GetBoolean(),Hints=r.GetProperty("hints").GetBoolean()};
        p.AutosaveLimit=r.TryGetProperty("autosaveLimit",out var limit)?limit.GetInt32():3;
        if(p.AutosaveLimit is <1 or >5)throw new InvalidDataException("Autosave limit must be1..5.");
        if(p.DeadZone is <2000 or >20000||p.TriggerThreshold is <10 or >240)throw new InvalidDataException("Invalid controller thresholds.");
        var players=r.GetProperty("players");if(players.GetArrayLength()!=2)throw new InvalidDataException("Two profiles required.");
        for(int i=0;i<2;i++)
        {
            var e=players[i];var v=p.Players[i];v.PadSlot=e.GetProperty("pad").GetInt32();v.Keyboard=e.GetProperty("keyboard").GetBoolean();v.Classic=e.GetProperty("classic").GetBoolean();v.Preset=e.GetProperty("preset").GetString()??"Custom";
            if(v.PadSlot is < -1 or >3)throw new InvalidDataException("Invalid pad slot.");
            var rows=e.GetProperty("bindings");if(rows.GetArrayLength()!=(int)GameAction.Count)throw new InvalidDataException("Incomplete action bank.");
            for(int j=0;j<rows.GetArrayLength();j++)v.Bindings[j]=ReadRow(rows[j]);
            foreach(var pair in e.GetProperty("direct").EnumerateObject()) {if(pair.Name.Length>80||v.Direct.Count>=512)throw new InvalidDataException("Invalid direct binding bank.");v.Direct.Add(pair.Name,ReadRow(pair.Value));}
            var quick=e.GetProperty("quick");if(quick.GetArrayLength()!=6)throw new InvalidDataException("Invalid party quick slots.");
            for(int c=0;c<6;c++){if(quick[c].GetArrayLength()!=4)throw new InvalidDataException("Invalid quick slots.");for(int q=0;q<4;q++){string id=quick[c][q].GetString()??"";if(id.Length>80)throw new InvalidDataException("Invalid move id.");v.Quick[c][q]=id;}}
        }
        if(p.Players[0].PadSlot>=0&&p.Players[0].PadSlot==p.Players[1].PadSlot)throw new InvalidDataException("A controller cannot belong to both players.");
        return p;
    }
    private static InputBinding[] ReadRow(JsonElement row)
    {
        if(row.GetArrayLength()!=3)throw new InvalidDataException("Three input slots required.");var result=new InputBinding[3];
        for(int i=0;i<3;i++){var b=row[i];if(b.GetArrayLength()!=2)throw new InvalidDataException("Invalid binding.");int kind=b[0].GetInt32(),code=b[1].GetInt32();if(kind<0||kind>3||code<0||code>(kind==3?23:kind==2?2:255))throw new InvalidDataException("Invalid input code.");result[i]=new((InputKind)kind,code);}return result;
    }
    public static ControlProfile Read(string path,ControlProfile fallback)
    {
        if(!File.Exists(path))return fallback;
        try{return Decode(File.ReadAllBytes(path));}
        catch(Exception e) when(e is JsonException or IOException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException or UnauthorizedAccessException)
        {Console.WriteLine("Invalid controls profile; using defaults: "+e.Message);return fallback;}
    }
    public static void Write(string path,ControlProfile p)
    {
        byte[] bytes=Encode(p);Decode(bytes);string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        for(var d=new DirectoryInfo(Path.GetDirectoryName(full)!);d is not null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected controls directory.");
        if(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected controls file.");
        string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}File.Move(temp,full,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
