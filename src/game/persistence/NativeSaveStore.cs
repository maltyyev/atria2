namespace Atria.Game.Persistence;

public enum SaveSlotStatus { Empty,Native,LegacyUnverified,Invalid,Unreadable }
public readonly record struct SaveGameDate(int Year,int Month,int Day);
public sealed record SaveSlotInfo(int Index,SaveSlotStatus Status,LegacySlotMetadata Metadata,SaveGameDate Date,string? Failure=null,bool Automatic=false,bool IsNew=false,DateTime WrittenUtc=default,string Reason="");
public sealed record SaveSlotContent(NativeSaveImage? Native,LegacySaveImage? Legacy);
/// <summary>Unbounded manual slots and a bounded rotating autosave bank in an explicit writable directory.</summary>
public sealed class NativeSaveStore
{
    private readonly string directory,originalDirectory;
    public NativeSaveStore(string directory,string originalResourceDirectory)
    {
        this.directory=Path.GetFullPath(directory);originalDirectory=ResolveDirectory(Path.GetFullPath(originalResourceDirectory));EnsureSafeDirectory();
    }
    public string GetSlotPath(int slot){CheckSlot(slot);return Path.Combine(directory,slot<0?$"auto{-slot:000}.sav":$"atria{slot:000}.sav");}
    public int NextManualSlot()
    {var ids=ExistingIds().Where(i=>i>=0).ToArray();return ids.Length==0?0:checked(ids.Max()+1);}
    private IEnumerable<int> ExistingIds()
    {
        EnsureSafeDirectory();if(!Directory.Exists(directory))yield break;
        foreach(string file in Directory.EnumerateFiles(directory,"*.sav"))
        {
            string name=Path.GetFileNameWithoutExtension(file);int id;
            if(name.StartsWith("atria",StringComparison.OrdinalIgnoreCase)&&int.TryParse(name.AsSpan(5),out id)&&id>=0&&name.Equals($"atria{id:000}",StringComparison.OrdinalIgnoreCase))yield return id;
            else if(name.StartsWith("auto",StringComparison.OrdinalIgnoreCase)&&int.TryParse(name.AsSpan(4),out id)&&id is >=1 and <=6&&name.Equals($"auto{id:000}",StringComparison.OrdinalIgnoreCase))yield return -id;
        }
    }
    public SaveSlotInfo[] InspectLibrary(bool includeNew)
    {
        var rows=ExistingIds().Distinct().Select(InspectOne).OrderByDescending(s=>s.WrittenUtc).ThenBy(s=>s.Index).ToList();
        if(includeNew)rows.Insert(0,new(-1,SaveSlotStatus.Empty,LegacySlotMetadata.Empty([]),default,IsNew:true));
        return rows.ToArray();
    }
    private SaveSlotInfo InspectOne(int id)
    {
        var empty=LegacySlotMetadata.Empty([]);DateTime written=File.GetLastWriteTimeUtc(GetSlotPath(id));
        try
        {
            var content=ReadSlot(id)??throw new IOException("Save disappeared.");
            var metadata=content.Native?.Metadata??LegacySlotMetadata.Parse(content.Legacy!.GetSlotMetadata());
            _=metadata.PrimaryName;_=metadata.SecondaryName;
            var state=content.Native?.State??content.Legacy!.ReadRecoveredState();string reason="";
            if(content.Native is {} native&&native.Context.Schema==SessionRuntimeCodec.Schema)reason=SessionRuntimeCodec.Decode(native.Context).SaveReason;
            return new(id,content.Native is null?SaveSlotStatus.LegacyUnverified:SaveSlotStatus.Native,metadata,new(state.Year,state.Month,state.Day),Automatic:id<0,WrittenUtc:written,Reason:reason);
        }
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {return new(id,SaveSlotStatus.Invalid,empty,default,e.Message,id<0,WrittenUtc:written);}
    }
    public void WriteAutomatic(NativeSaveImage image,int limit)
    {
        if(limit is <1 or >5)throw new ArgumentOutOfRangeException(nameof(limit));
        // A sixth temporary generation permits commit-before-prune even with a limit of five.
        var used=ExistingIds().ToHashSet();int id=Enumerable.Range(1,6).Select(i=>-i).FirstOrDefault(i=>!used.Contains(i));
        if(id==0){PruneAutomatic(limit);used=ExistingIds().ToHashSet();id=Enumerable.Range(1,6).Select(i=>-i).First(i=>!used.Contains(i));}
        WriteNative(id,image);PruneAutomatic(limit,id);
    }
    public void PruneAutomatic(int limit,int newest=0)
    {
        if(limit is <1 or >5)throw new ArgumentOutOfRangeException(nameof(limit));EnsureSafeDirectory();
        foreach(int id in ExistingIds().Where(i=>i<0).OrderByDescending(i=>i==newest).ThenByDescending(i=>File.GetLastWriteTimeUtc(GetSlotPath(i))).Skip(limit).ToArray())
        {string path=GetSlotPath(id);if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected autosave file.");File.Delete(path);}
    }
    public SaveSlotContent? ReadSlot(int slot)
    {
        EnsureSafeDirectory();string path=GetSlotPath(slot);if(!File.Exists(path))return null;
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>NativeSaveImage.MaximumSize||file.Length<1)throw new InvalidDataException("Save file length exceeds supported bounded formats.");
        var bytes=new byte[(int)file.Length];file.ReadExactly(bytes);if(file.ReadByte()!=-1)throw new InvalidDataException("Save file changed length while being read.");
        return NativeSaveImage.HasNativeSignature(bytes)?new(NativeSaveImage.Parse(bytes),null):new(null,LegacySaveImage.Parse(bytes));
    }
    public SaveSlotInfo[] InspectSlots(ReadOnlySpan<byte> emptyLabel)
    {
        var empty=LegacySlotMetadata.Empty(emptyLabel);var slots=new SaveSlotInfo[5];
        for(int i=0;i<5;i++)
        {
            try
            {
                var content=ReadSlot(i);
                if(content is null){slots[i]=new(i,SaveSlotStatus.Empty,empty,default);continue;}
                var metadata=content.Native?.Metadata??LegacySlotMetadata.Parse(content.Legacy!.GetSlotMetadata());
                _=metadata.PrimaryName;_=metadata.SecondaryName;
                var state=content.Native?.State??content.Legacy!.ReadRecoveredState();
                slots[i]=new(i,content.Native is null?SaveSlotStatus.LegacyUnverified:SaveSlotStatus.Native,metadata,new(state.Year,state.Month,state.Day));
            }
            catch(InvalidDataException error){slots[i]=new(i,SaveSlotStatus.Invalid,empty,default,error.Message);}
            catch(IOException error){slots[i]=new(i,SaveSlotStatus.Unreadable,empty,default,error.Message);}
            catch(UnauthorizedAccessException error){slots[i]=new(i,SaveSlotStatus.Unreadable,empty,default,error.Message);}
        }
        return slots;
    }
    public void WriteNative(int slot,NativeSaveImage image)=>WriteAtomic(slot,image.Encode());
    public void WriteLegacy(int slot,LegacySaveImage image)=>WriteAtomic(slot,image.Encode());
    private void WriteAtomic(int slot,ReadOnlySpan<byte> bytes)
    {
        EnsureSafeDirectory();string destination=GetSlotPath(slot);Directory.CreateDirectory(directory);EnsureSafeDirectory();
        string temporary=Path.Combine(directory,$".atria{slot:000}.{Guid.NewGuid():N}.tmp");
        try
        {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough))
            {file.Write(bytes);file.Flush(true);}
            if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
        }
        finally
        {
            try {if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private void EnsureSafeDirectory()
    {
        string resolved=ResolveDirectory(directory);
        if(string.Equals(resolved,originalDirectory,StringComparison.OrdinalIgnoreCase)||resolved.StartsWith(originalDirectory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Save output cannot be the original resource directory or any child directory.");
    }
    private static string ResolveDirectory(string path)
    {
        string current=Path.GetPathRoot(path)??throw new InvalidDataException("Save path has no root.");
        foreach(string part in path[current.Length..].Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))
        {
            if(part.Length==0)continue;current=Path.Combine(current,part);
            var info=new DirectoryInfo(current);if(info.LinkTarget is not null){var target=info.ResolveLinkTarget(true)??throw new InvalidDataException("Save path link cannot be resolved.");current=target.FullName;}
        }
        return Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar);
    }
    private static void CheckSlot(int slot){if(slot< -6)throw new ArgumentOutOfRangeException(nameof(slot));}
}

