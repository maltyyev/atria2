using Atria.Game.Settings;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Settings;

public static class SettingsTests
{
    public static string Run(string root)
    {
        var state = new NativeSettingsState();
        var controller = new SettingsController(state);
        controller.Enter();
        List<SettingsEffect> effects = [];
        List<SettingsKey> queries = [];
        void Tick(params SettingsKey[] keys)
        {
            queries.Clear();
            effects.Clear();
            controller.Input(key =>
            {
                queries.Add(key);
                return keys.Contains(key);
            }, effects.Add);
        }

        Tick(SettingsKey.Left, SettingsKey.Confirm);
        Check(queries.SequenceEqual(new[] { SettingsKey.Left }) && effects.Count == 0, "left boundary suppresses all later queries");
        Tick(SettingsKey.Up, SettingsKey.Cancel);
        Check(state.Music == 3
&& effects.Select(x => x.Kind).SequenceEqual(new[] { SettingsEffectKind.Sound, SettingsEffectKind.ApplyMusicVolume, SettingsEffectKind.Sound, SettingsEffectKind.Close, SettingsEffectKind.UiDirty }), "adjust then cancel preserves live volume and ordered effects");
        controller.Enter();
        Tick(SettingsKey.Right);
        Tick(SettingsKey.Right);
        Tick(SettingsKey.Down, SettingsKey.Cancel);
        Check(state.ThirdOption == 1 && controller.ThirdOption == 2 && !effects.Any(x => x.Kind == SettingsEffectKind.ReleaseSprites), "cancel keeps third option staged and native release omission explicit");
        controller.Enter();
        Tick(SettingsKey.Right);
        Tick(SettingsKey.Right);
        Tick(SettingsKey.Down, SettingsKey.Confirm);
        Check(state.ThirdOption == 2 && effects[1].Kind == SettingsEffectKind.Sound && effects[2].Kind == SettingsEffectKind.ReleaseSprites, "confirm commits third and releases sprites");
        controller.Enter();
        Tick(SettingsKey.Up);
        Tick(SettingsKey.Up, SettingsKey.Down);
        Check(state.Music == 4 && !queries.Contains(SettingsKey.Down), "up at cap suppresses down");
        var frames = Enumerable.Range(0, 4).Select(_ => controller.AdvanceFrame()).ToArray();
        Check(frames.Take(3).All(x => x.Any(v => v.Frame == 3 && !v.Half)) && frames[3].Any(v => v.Frame == 3 && v.Half) && controller.FlashDirection == 0, "three full flash passes then one half");
        Check(NativeSettingsState.Parse(state.Encode()).Music == 4, "versioned snapshot");
        bool rejected = false;
        try
        {
            NativeSettingsState.Parse([1, 5, 4, 1]);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Check(rejected, "invalid config is rejected");
        var controls = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        byte original = controls[1, 7];
        var remapped = LocalSettingsCodec.Remap(controls, 1, 7, 0x9d);
        Check(remapped[1, 7] == 0x9d && remapped[0, 4] == controls[0, 4] && controls[1, 7] == original, "physical extended scan remap preserves original bindings");
        var decoded = LocalSettingsCodec.Parse(LocalSettingsCodec.Encode(state, remapped));
        Check(decoded.Bindings[1, 7] == 0x9d && decoded.Settings.ThirdOption == 2, "bounded modern config preserves both domains");
        Check(SettingsAudio.MusicGain(0) == 1f / 32 && SettingsAudio.MusicGain(4) == .5f && SettingsAudio.EffectsHundredthDecibels(1) == -1500, "actual native volume tables");
        using (var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000")))
        using (var bitmaps = BinderArchive.Open(Path.Combine(root, "AT2BMP.000")))
        {
            var sprites = SpriteFile.Decode(archive.ReadMember(archive.FindResource("OPTION"u8)));
            var bitmap = BmpImage.Decode(bitmaps.ReadMember(bitmaps.FindResource("OPTION"u8)));
            Check(bitmap.Width == 640 && bitmap.Height == 480 && sprites.Frames.Length >= 19, "actual option resources cover native draw contract");
            ushort[] pixels = new ushort[640 * 480];
            controller.Enter();
            SettingsRenderer.Draw(sprites, controller.AdvanceFrame(), pixels);
            Check(pixels.Any(x => x != 0), "actual OPTION raster executes");
        }

        string output = Path.Combine(Path.GetTempPath(), "atria-settings-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalSettingsStore(output, root);
            Check(store.Read() is null, "absent local config");
            store.Write(state, remapped);
            Check(store.Read()!.Value.Bindings[1, 7] == 0x9d, "actual atomic config persistence");
            state.Set(1, 2, 1);
            store.Write(state, controls);
            Check(store.Read()!.Value.Settings.Music == 1 && Directory.GetFiles(output).Length == 1, "same-directory replacement has no retained temporary files");
            File.WriteAllBytes(store.ConfigPath, new byte[29]);
            Check(store.Read() is null, "oversize config falls back without blocking startup");
            rejected = false;
            try
            {
                new LocalSettingsStore(root, root).Read();
            }
            catch (IOException)
            {
                rejected = true;
            }

            Check(rejected, "original resource write directory forbidden");
        }
        finally
        {
            // Only explicit files in the fresh fixture directory; no recursive cleanup.
            if (Directory.Exists(output))
            {
                foreach (string file in Directory.GetFiles(output))
                {
                    File.Delete(file);
                }

                Directory.Delete(output);
            }
        }

        return "Settings: lazy input priority, live cancel, staged commit, flash timing, bounded snapshot";
    }

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
