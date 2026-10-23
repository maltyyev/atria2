using System.Diagnostics;
using System.Text;
using Atria.Audio;
using Atria.Platform;
using Atria.Render;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Menu;

/// <summary>Native menu host. Game transitions are supplied by the owning session, not fabricated here.</summary>
public static class MainMenuRuntime
{
    public static int Run(string resourceRoot, string musicRoot, double seconds, Action<MenuCommand> transition, bool muted)
    {
        using var bitmaps = BinderArchive.Open(Path.Combine(resourceRoot, "AT2BMP.000"));
        using var sprites = BinderArchive.Open(Path.Combine(resourceRoot, "AT2SPR.000"));
        using var animations = BinderArchive.Open(Path.Combine(resourceRoot, "ANI.000"));
        using var waves = BinderArchive.Open(Path.Combine(resourceRoot, "AT2WAV.000"));
        var backgroundImage = BmpImage.Decode(Read(bitmaps, "MENUBG"));
        if (backgroundImage.Width != 640 || backgroundImage.Height != 480) throw new InvalidDataException("Invalid menu background dimensions.");
        ushort[] background = new ushort[640 * 480], framebuffer = new ushort[640 * 480];
        uint[] pixels = new uint[640 * 480];
        for (int i = 0; i < background.Length; i++)
        {
            uint rgb = backgroundImage.Pixels[i];
            background[i] = (ushort)(((rgb >> 8) & 0xf800) | ((rgb >> 5) & 0x07e0) | ((rgb >> 3) & 31));
        }
        var menu = SpriteFile.Decode(Read(sprites, "MENU"));
        var fire = SpriteFile.Decode(Read(sprites, "MENUFIRE"));
        var animation = AnimationFile.Decode(Read(animations, "MENUFIRE"));
        var etc = SpriteFile.Decode(Read(sprites, "ETC"));
        // 0x423660 explicitly sets text mode0: large ENG font, regardless of prior scene font.
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(resourceRoot, "ENG.FNT")), false);
        var modal = new MainMenuModalRenderer(etc, font, File.ReadAllBytes(Path.Combine(resourceRoot, "TEXTmsg.dat")));
        var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(resourceRoot, "BATTLE.CFG")));
        using var window = new NativeWindow("Atria II — native menu verification", 2560, 1440);
        using var renderer = new D3D11Presenter(window.Handle, 2560, 1440);
        using var audio = new AudioDevice();
        using var transport = new FileMusicTransport(audio, musicRoot, muted ? 0 : 1);
        var music = new MusicController(transport);
        SoundEffectBank[] effects = SoundEffectBank.UiMemberNames.Select(name => audio.LoadEffect(Read(waves, name))).ToArray();
        var controller = new MainMenuController(); controller.Enter();
        // Menu/exploration strict >20 integer milliseconds. This does not specify battle simulation timing.
        var clock = new LogicalFrameClock(TimeSpan.FromMilliseconds(21));
        var timer = Stopwatch.StartNew();
        bool[] pending = new bool[256];
        long presents = 0;
        long observedKeys = 0;
        void Emit(MenuCommand command)
        {
            if (command.Effect == MenuEffect.Sound)
            {
                if ((uint)command.Value >= (uint)effects.Length) throw new InvalidDataException("Unknown UI sound.");
                effects[command.Value].SetCurrentVolume(muted ? -10000 : 0);
                effects[command.Value].Play();
                return;
            }
            transition(command);
        }
        while (!window.Closed && timer.Elapsed.TotalSeconds < seconds)
        {
            window.Pump();
            if(observedKeys != window.KeyDownCount)
            {
                observedKeys=window.KeyDownCount;
                Console.WriteLine($"Menu input: virtualKey={window.LastVirtualKey}, scan={window.LastScanCode}, focused={window.Focused}.");
            }
            (int width, int height) = window.ClientSize;
            if (!window.Focused || width == 0 || height == 0)
            {
                Array.Clear(pending); clock.SuspendAt(timer.Elapsed); Thread.Sleep(10); continue;
            }
            for (int key = 0; key < pending.Length; key++) pending[key] |= window.ScanPressed[key];
            bool changed = false;
            while (clock.TakeDueFrame(timer.Elapsed))
            {
                // Rendering is the native logical callback; pure GPU presents below never mutate menu state.
                music.Update(1);
                MainMenuComposer.Draw(controller.AdvanceLogicalFrame(), background, menu, animation, fire, framebuffer, modal.Draw);
                controller.Input(new(pending[bindings.Next], pending[bindings.Previous], pending[bindings.Confirm], pending[bindings.Cancel]), true, Emit);
                Array.Clear(pending); changed = true;
            }
            if (changed) for (int i = 0; i < pixels.Length; i++) pixels[i] = SpriteRaster.ToBgra32(framebuffer[i]);
            renderer.Resize(width, height); renderer.Present(pixels); presents++;
        }
        Console.WriteLine($"Native menu: presents={presents}, logicalFrames={clock.Frames}, seconds={timer.Elapsed.TotalSeconds:F3}, selected={controller.Selection}, confirmation={controller.Confirmation}, muted={muted}.");
        return presents > 0 ? 0 : 3;
    }
    private static byte[] Read(BinderArchive archive, string name)
    {
        int index = archive.FindResource(Encoding.ASCII.GetBytes(name));
        if (index < 0) throw new FileNotFoundException("Required menu resource is missing.", name);
        return archive.ReadMember(index);
    }
}
