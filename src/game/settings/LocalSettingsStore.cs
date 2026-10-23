using Atria.Resources;

namespace Atria.Game.Settings;

/// <summary>Explicit modern output path, bounded read and same-directory atomic replacement.</summary>
public sealed class LocalSettingsStore(string outputDirectory,string originalResourceDirectory)
{
    private readonly string directory=Path.GetFullPath(outputDirectory);
    private readonly string originals=Path.GetFullPath(originalResourceDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
    public string ConfigPath=>Path.Combine(directory,"atria.settings");
    private void CheckDirectory()
    {
        string target=directory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(target.StartsWith(originals,StringComparison.OrdinalIgnoreCase)) throw new IOException("Settings output is inside original resources.");
        for(DirectoryInfo? part=new(directory);part is not null;part=part.Parent)
            if(part.Exists&&(part.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Settings output contains a redirected directory.");
    }
    public (NativeSettingsState Settings,ControlBindings Bindings)? Read()
    {
        CheckDirectory();if(!File.Exists(ConfigPath))return null;
        if((File.GetAttributes(ConfigPath)&FileAttributes.ReparsePoint)!=0) throw new IOException("Settings file is redirected.");
        using var file=new FileStream(ConfigPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length!=LocalSettingsCodec.EncodedSize) throw new InvalidDataException("Invalid local config size.");
        byte[] bytes=new byte[LocalSettingsCodec.EncodedSize];file.ReadExactly(bytes);return LocalSettingsCodec.Parse(bytes);
    }
    public void Write(NativeSettingsState settings,ControlBindings bindings)
    {
        CheckDirectory();Directory.CreateDirectory(directory);CheckDirectory();
        string temporary=Path.Combine(directory,$".atria.settings.{Guid.NewGuid():N}.tmp");
        try
        {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {file.Write(LocalSettingsCodec.Encode(settings,bindings));file.Flush(true);}
            File.Move(temporary,ConfigPath,true);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
}
