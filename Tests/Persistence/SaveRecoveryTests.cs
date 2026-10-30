using System.Security.Cryptography;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.World;

namespace Atria.Tests.Persistence;

internal static class SaveRecoveryTests
{
    public static string Run(string root)
    {
        string directory = Path.GetFullPath(Path.Combine(root, "..", "audit", "save-recovery", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var store = new NativeSaveStore(directory, root);
        var state = new GameState { Mode = 300, Money = 123 };
        var metadata = LegacySlotMetadata.Empty([]).WithCurrent("Town"u8, "Region"u8, 300, 0, 1, 2, []);
        var image = NativeSaveImage.Capture(state, 300, new WorldAreaStore(),
            LegacyShopProjection.DecodeAll(new byte[30 * 612]), metadata, new("fixture", 1, [1]));
        byte[] valid = image.Encode();
        byte[] corrupt = (byte[])valid.Clone();
        corrupt[^1] ^= 1;
        byte[][] damaged = [corrupt, valid[..12], [0, 1, 2], new byte[NativeSaveImage.MaximumSize + 1]];
        File.WriteAllBytes(store.GetSlotPath(0), valid);

        // Fill every generation to exercise cleanup before the next write, not just after it.
        for (int i = 1; i <= 6; i++)
        {
            string path = store.GetSlotPath(-i);
            File.WriteAllBytes(path, i <= damaged.Length ? damaged[i - 1] : valid);
            File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddMinutes(i));
        }
        store.WriteAutomatic(image, 1);
        for (int i = 0; i < damaged.Length; i++)
            VerifyBackup(directory, $"auto{i + 1:000}", damaged[i]);
        for (int i = 0; i < 12; i++)
            store.WriteAutomatic(image, 1);
        Check(store.InspectLibrary(false).Count(s => s.Automatic) == 1, "rotation resumes beyond all six generations");
        Check(File.ReadAllBytes(store.GetSlotPath(0)).AsSpan().SequenceEqual(valid), "manual save unchanged by rotation");

        File.WriteAllBytes(store.GetSlotPath(1), corrupt);
        store.WriteNative(1, image);
        VerifyBackup(directory, "atria001", corrupt);
        Check(store.ReadSlot(1)!.Native!.State.Money == 123, "corrupt manual overwrite keeps recovery bytes and commits new save");

        File.WriteAllBytes(store.GetSlotPath(1), corrupt);
        store.WriteNative(1, image);
        VerifyBackup(directory, "atria001", corrupt);
        string recovery = Directory.GetFiles(Path.Combine(directory, "recovery-backups"), "atria001-*.sav").Single();
        File.WriteAllBytes(recovery, new byte[corrupt.Length]);
        File.WriteAllBytes(store.GetSlotPath(1), corrupt);
        Reject(() => store.WriteNative(1, image));
        Check(File.ReadAllBytes(store.GetSlotPath(1)).AsSpan().SequenceEqual(corrupt), "damaged existing recovery copy cannot authorize overwrite");

        // A failed recovery copy must never permit replacement or removal of its source.
        string blocked = Path.Combine(directory, "blocked");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "recovery-backups"), "not a directory");
        var blockedStore = new NativeSaveStore(blocked, root);
        File.WriteAllBytes(blockedStore.GetSlotPath(0), corrupt);
        File.WriteAllBytes(blockedStore.GetSlotPath(-1), corrupt);
        File.WriteAllBytes(blockedStore.GetSlotPath(-2), valid);
        File.SetLastWriteTimeUtc(blockedStore.GetSlotPath(-1), DateTime.UnixEpoch);
        Reject(() => blockedStore.WriteNative(0, image));
        Reject(() => blockedStore.PruneAutomatic(1));
        Check(File.ReadAllBytes(blockedStore.GetSlotPath(0)).AsSpan().SequenceEqual(corrupt), "failed backup preserves manual source");
        Check(File.ReadAllBytes(blockedStore.GetSlotPath(-1)).AsSpan().SequenceEqual(corrupt), "failed backup preserves automatic source");
        return "Save recovery passed: checksum/truncation/signature/oversize, full-bank rotation, manual overwrite, failed backup preserves originals.";
    }

    private static void VerifyBackup(string directory, string name, byte[] bytes)
    {
        string path = Path.Combine(directory, "recovery-backups", $"{name}-{Convert.ToHexString(SHA256.HashData(bytes))}.sav");
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "byte-exact recovery copy " + name);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("Failed backup unexpectedly allowed save replacement.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Save recovery: " + message);
    }
}
