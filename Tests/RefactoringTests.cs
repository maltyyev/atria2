using Atria.Game.Controls;
using Atria.Game.Settings;
using Atria.Platform;
using Atria.Platform.Win32;
using Atria.Render;
using Atria.Resources;

namespace Atria.Tests;

internal static class RefactoringTests
{
    public static string Run(string resourceRoot)
    {
        var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(resourceRoot, "BATTLE.CFG")));
        string parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "probe"));
        string directory = Path.Combine(parent, "preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var legacyStore = new LocalSettingsStore(directory, resourceRoot);
            var audio = new NativeSettingsState();
            audio.Set(1, 2, 2);
            legacyStore.Write(audio, bindings);
            var controls = ControlProfile.Defaults(bindings);
            controls.DeadZone = 12000;
            string controlsPath = Path.Combine(directory, "controls.json");
            ControlProfileStore.Write(controlsPath, controls);
            byte[] legacyBefore = File.ReadAllBytes(legacyStore.ConfigPath);
            var store = new UserPreferencesStore(directory, resourceRoot);
            var migrated = store.Read(bindings);
            Check(migrated.Audio.Music == 1 && migrated.Controls.DeadZone == 12000, "both legacy settings files migrate");
            audio.Set(3, 4, 1);
            controls.AutosaveLimit = 5;
            store.Write(new(controls, audio, bindings));
            var saved = store.Read(bindings);
            Check(saved.Audio.Music == 3 && saved.Controls.AutosaveLimit == 5, "audio and controls commit together");
            Check(File.ReadAllBytes(legacyStore.ConfigPath).AsSpan().SequenceEqual(legacyBefore), "migration preserves original config");
            byte[] before = File.ReadAllBytes(store.FilePath);
            bool rejected = false;
            using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try
                {
                    store.Write(new(controls, audio, bindings));
                }
                catch (IOException)
                {
                    rejected = true;
                }
                catch (UnauthorizedAccessException)
                {
                    rejected = true;
                }
            }

            Check(rejected && File.ReadAllBytes(store.FilePath).AsSpan().SequenceEqual(before), "failed replacement preserves the complete old document");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "failed save leaves no temporary file");
            File.WriteAllText(controlsPath, "{\"version\":999}");
            Check(ReferenceEquals(ControlProfileStore.Read(controlsPath, controls), controls), "unsupported control version falls back");
            File.WriteAllText(store.FilePath, "{\"version\":999}");
            Check(store.Read(bindings).Audio.Music == 1, "corrupt preferences recover legacy settings");
            File.WriteAllBytes(legacyStore.ConfigPath, [1, 2]);
            Check(legacyStore.Read() is null, "truncated old settings do not block launch");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }

            Directory.Delete(directory);
        }

        Check(D3D11Presenter.IsDeviceLost(unchecked((int)0x887A0005)), "device removal is recoverable");
        Check(!D3D11Presenter.IsDeviceLost(unchecked((int)0x80070057)), "invalid arguments are not swallowed as device loss");
        using var window = new NativeWindow("Atria renderer lifecycle verification", 640, 480);
        using var presenter = new D3D11Presenter(window.Handle, 640, 480);
        presenter.SetFrameSize(380, 240);
        presenter.Present(new uint[380 * 240]);
        var viewport = presenter.ContentViewport;
        presenter.RecreateDevice();
        Check(presenter.ContentViewport == viewport, "device recreation preserves source dimensions and viewport");
        presenter.Present(new uint[380 * 240]);
        presenter.SetFrameSize(640, 480);
        presenter.Present(new uint[640 * 480]);
        return "Refactoring: legacy migration, atomic combined settings, corrupt-config fallback, failed-write preservation and real D3D11 recreation passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Refactoring: " + message);
        }
    }
}
