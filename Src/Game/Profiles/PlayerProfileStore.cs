using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Atria.Game.Profiles;

internal sealed record PlayerProfile(string Id, long Number, string Name);

/// <summary>Names are display text; only generated IDs are used as directory names.</summary>
internal sealed class PlayerProfileStore
{
    internal const int MaximumNameLength = 40;
    private const string FirstId = "00000000000000000000000000000001";
    private readonly string root;
    private readonly string catalogPath;
    private PlayerProfile[] profiles = [];
    public IReadOnlyList<PlayerProfile> Profiles => profiles;
    public string ActiveId { get; private set; } = "";
    public PlayerProfile Active => profiles.Single(p => p.Id == ActiveId);
    public string DirectoryFor(PlayerProfile profile)
    {
        ValidateId(profile.Id);
        return Path.Combine(root, "profiles", profile.Id);
    }

    public PlayerProfileStore(string root, Func<long, string> defaultName)
    {
        this.root = Path.GetFullPath(root);
        catalogPath = Path.Combine(this.root, "profiles.json");
        Directory.CreateDirectory(this.root);
        if (!File.Exists(catalogPath))
        {
            string profileRoot = Path.Combine(this.root, "profiles");
            if (Directory.Exists(profileRoot) && Directory.EnumerateDirectories(profileRoot)
                .Any(p => Path.GetFileName(p) is not (FirstId or FirstId + ".migration")))
                throw new InvalidDataException("Profile catalog is missing; existing profile directories were preserved.");
            var first = new PlayerProfile(FirstId, 1, ValidateName(defaultName(1)));
            string destination = DirectoryFor(first);
            if (!Directory.Exists(destination))
            {
                string staging = destination + ".migration";
                Directory.CreateDirectory(staging);
                foreach (string kind in new[] { "config", "saves" })
                {
                    string source = Path.Combine(this.root, kind);
                    if (Directory.Exists(source)) CopyTree(source, Path.Combine(staging, kind));
                }
                // Commit the complete copy before the catalog; old folders remain a recovery copy.
                Directory.Move(staging, destination);
            }
            Save([first], first.Id);
        }
        Read();
    }

    public string NextName(Func<long, string> defaultName) => defaultName(NextNumber);
    private long NextNumber => checked(profiles.Max(p => p.Number) + 1);

    public PlayerProfile Create(string name)
    {
        var profile = new PlayerProfile(Guid.NewGuid().ToString("N"), NextNumber, ValidateName(name));
        var updated = profiles.Append(profile).ToArray();
        Directory.CreateDirectory(DirectoryFor(profile));
        Save(updated, ActiveId);
        profiles = updated;
        return profile;
    }

    public void Rename(string id, string name)
    {
        _ = profiles.Single(p => p.Id == id);
        var updated = profiles.Select(p => p.Id == id ? p with { Name = ValidateName(name) } : p).ToArray();
        Save(updated, ActiveId);
        profiles = updated;
    }

    public void Select(string id)
    {
        _ = profiles.Single(p => p.Id == id);
        Save(profiles, id);
        ActiveId = id;
    }

    internal static string ValidateName(string value)
    {
        string name = value.Trim().Normalize(NormalizationForm.FormC);
        if (name.Length == 0 || new StringInfo(name).LengthInTextElements > MaximumNameLength
            || name.EnumerateRunes().Any(r => Rune.IsControl(r) || Rune.GetUnicodeCategory(r) == UnicodeCategory.Format))
            throw new ArgumentException("Invalid player profile name.");
        return name;
    }

    private static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid player profile ID.");
    }

    private void Read()
    {
        using var file = File.OpenRead(catalogPath);
        using var json = JsonDocument.Parse(file);
        var document = json.RootElement;
        if (document.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported player profile catalog.");
        var loaded = new List<PlayerProfile>();
        foreach (var entry in document.GetProperty("profiles").EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()!;
            ValidateId(id);
            long number = entry.GetProperty("number").GetInt64();
            loaded.Add(new(id, number, ValidateName(entry.GetProperty("name").GetString()!)));
        }
        string active = document.GetProperty("active").GetString()!;
        if (loaded.Count == 0 || loaded.Any(p => p.Number <= 0)
            || loaded.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != loaded.Count
            || loaded.Select(p => p.Number).Distinct().Count() != loaded.Count || !loaded.Any(p => p.Id == active))
            throw new InvalidDataException("Inconsistent player profile catalog; existing data was preserved.");
        profiles = loaded.ToArray();
        ActiveId = active;
    }

    private void Save(IReadOnlyList<PlayerProfile> entries, string active)
    {
        string temporary = catalogPath + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var writer = new Utf8JsonWriter(file, new()
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                writer.WriteString("active", active);
                writer.WriteStartArray("profiles");
                foreach (var entry in entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", entry.Id);
                    writer.WriteNumber("number", entry.Number);
                    writer.WriteString("name", entry.Name);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            file.Flush(true);
        }
        File.Move(temporary, catalogPath, true);
    }

    private static void CopyTree(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Profile migration cannot follow directory links.");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Profile migration cannot follow file links.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
