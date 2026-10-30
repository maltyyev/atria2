using System.Text;
using Atria.Audio;
using Atria.Game.Dialogue;
using Atria.Game.Dungeon;
using Atria.Game.Navigation;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Localization;
using Atria.Platform.Win32;
using Atria.Render;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    internal static string VerifyExpedition(string root, string musicRoot, string fixture, Action<string, ushort[]> capture)
    {
        string output = Path.GetFullPath(Path.Combine(root, "..", "audit", "expedition-0170", Guid.NewGuid().ToString("N")));
        using var window = new NativeWindow("Atria expedition verification", 2560, 1440);
        using var presenter = new D3D11Presenter(window.Handle, 2560, 1440);
        using var audio = new AudioDevice();
        using var transport = new FileMusicTransport(audio, musicRoot, 0);
        using var faces = new FaceSpriteSource(Path.Combine(root, "face.SPR"));
        var session = new GameSession(root, window, presenter, audio, transport, faces, 90, true, null, output);
        var previousLanguage = GameText.Current;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(session.PersistenceStore.GetSlotPath(0))!);
            File.Copy(fixture, session.PersistenceStore.GetSlotPath(0));
            session.LoadSessionSlot(0);
            using var definitions = GameData.Open("AuthoredDungeons");
            foreach (var definition in definitions.RootElement.EnumerateObject())
            {
                session.LoadExplorationReturnMap("ground7.amf"u8.ToArray());
                session.state.WorldX = 4;
                session.state.WorldY = 5;
                session.CommitLocation(LocationKind.World);
                session.state.ScriptActive = session.state.DungeonEntryPending = 0;
                session.state.PendingDungeonMap = Encoding.ASCII.GetBytes(definition.Name + ".amf");
                session.state.PendingDungeonScript = Encoding.ASCII.GetBytes(definition.Name + ".scr");
                session.StartDungeonFromWorld(true);
                Check(session.dungeonEntry!.Source == DungeonEntrySource.WorldScript, "Core uses world-script entry, never absent town artwork");
                // Drive initial controller dispatch: rendering alone would miss a missing enter handler.
                for (int frame = 0; frame < 8; frame++)
                {
                    foreach (var command in session.dungeon!.Input(default, _ => false, _ => false))
                        session.HandleDungeonCommand(command);
                    if (session.state.ScriptActive != 0)
                        session.RunScript(session.scriptMember, session.pendingHandler);
                    session.ComposeScene(session.state, session.host.FrameBuffer);
                }
                Check(!session.dungeon!.ScriptPending && session.state.ScriptActive == 0, "initial dungeon callback finishes");
                foreach (var point in definition.Value.GetProperty("points").EnumerateArray())
                {
                    var at = point.GetProperty("at");
                    session.state.WorldX = at[0].GetInt32();
                    session.state.WorldY = at[1].GetInt32();
                    session.dungeon!.Visibility.Arrival(session.state.WorldX, session.state.WorldY, 10);
                    session.state.DungeonFogDirty = 1;
                    session.state.NarrativeHistory.Add(point.GetProperty("completedScene").GetString()!);
                    session.RefreshAuthoredDungeon();
                    var saved = session.CaptureSaveImage(GameMode.Dungeon, true, "");
                    session.PersistenceStore.WriteNative(1, saved);
                    session.LoadSessionSlot(1);
                    Check(session.dungeon!.RuntimeMap.Read(session.state.WorldY * 10 + session.state.WorldX, 88) == 0,
                        "loaded objective remains cleared");
                    Check(session.CaptureSaveImage(GameMode.Dungeon, true, "").Context.Payload.Span.SequenceEqual(saved.Context.Payload.Span),
                        "real dungeon session restores exactly");
                }
                foreach (string locale in new[] { "ru-RU", "be-BY" })
                {
                    GameText.Use(new LocalizationCatalog(Path.Combine(root, "..", "Localization"), locale));
                    var row = session.PersistenceStore.InspectLibrary(false).Single(row => row.Index == 1);
                    Check(row.Status == SaveSlotStatus.Native && row.Location?.Text == session.dungeonTitle.Text,
                        "save list uses the authored localized dungeon title");
                    session.ComposeScene(session.state, session.host.FrameBuffer);
                    session.ComposeScene(session.state, session.host.FrameBuffer);
                    capture(Path.Combine(output, definition.Name + "-" + locale + ".bmp"), session.host.FrameBuffer.ToArray());
                    session.host.Present();
                    window.Pump();
                }
                session.LeaveDungeon();
                Check(session.state.Mode == GameMode.World && session.map!.AreaIndex == 6
                    && session.state.WorldX == 4 && session.state.WorldY == 5, "loaded dungeon exits to the actual Core tile");
                session.ComposeScene(session.state, session.host.FrameBuffer);
                session.host.Present();
                window.Pump();
            }
            return "Expedition real session PASS: both dungeon entry/save/load/render/return paths and RU/BE save titles. Isolated data: " + output;
        }
        finally
        {
            GameText.Use(previousLanguage);
            session.DisposeProgression();
            session.controls.Dispose();
            session.StopAudio();
        }

        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Expedition session: " + message);
        }
    }
}
