using System.Buffers.Binary;
using System.Diagnostics;
using Atria.Game.Interiors;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Persistence;

public static class PersistenceRuntimeTests
{
    public static string RunMenuRaster(string root)
    {
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var empty = LegacySlotMetadata.Empty(messages[361].Span);
        var filled = empty.WithCurrent("Town"u8, "Area"u8, 400, 0, 1, 1, []);
        RasterChecks(root, messages, filled, empty, (ok, message) =>
        {
            if (!ok)
            {
                throw new InvalidOperationException(message);
            }
        });
        return "Save/load raster regression passed: empty rows visible, selected palette, occupied row and modal rendering.";
    }

    public static string Run(string resourceRoot)
    {
        RuntimeSnapshotTests.Run(resourceRoot);
        void Check(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidOperationException("Persistence runtime: " + message);
            }
        }

        void Reject(Action action, string message)
        {
            bool rejected = false;
            try
            {
                action();
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Check(rejected, message);
        }

        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(resourceRoot, "TEXTmsg.dat")));
        byte[] metadataBytes = Enumerable.Repeat((byte)0x5a, 133).ToArray();
        metadataBytes[13] = metadataBytes[63] = 0;
        Put(metadataBytes, 113, 0x44332211);
        var metadata = LegacySlotMetadata.Parse(metadataBytes).WithCurrent("Area\0padding"u8, "Town\0padding"u8, 300, 0, 1, 9, " suffix"u8);
        byte[] encodedMetadata = metadata.Encode();
        Check(metadata.PrimaryName.Span.SequenceEqual("Area suffix"u8) && metadata.SecondaryName.Span.SequenceEqual("Town suffix"u8), "writer truncates input atNUL then appends world suffix");
        Check(encodedMetadata[0] == 0x5a && encodedMetadata[40] == 0x5a && metadata.Opaque113 == 0x44332211
&& BinaryPrimitives.ReadInt32LittleEndian(encodedMetadata.AsSpan(117)) == 1 && BinaryPrimitives.ReadInt32LittleEndian(encodedMetadata.AsSpan(129)) == 9, "known metadata offsets and retained opaque/name tails");
        var empty = LegacySlotMetadata.Empty(messages[361].Span);
        Check(empty.Present == 0 && empty.AreaIndex == -1 && empty.TownId == -1, "42ab90 empty scalar constants");
        byte[] baselineBytes = new byte[30 * 612], changedBytes = new byte[30 * 612];
        Put(baselineBytes, 12 * 612 + 204 + 8, 111);
        Put(baselineBytes, 12 * 612 + 204 + 12, 222);
        Put(changedBytes, 12 * 612 + 204 + 8, 333);
        Put(changedBytes, 12 * 612 + 204 + 12, 444);
        Put(baselineBytes, 29 * 612, 1);
        Put(baselineBytes, 29 * 612 + 4, 10);
        Put(baselineBytes, 29 * 612 + 104, 7);
        Put(changedBytes, 29 * 612, 1);
        Put(changedBytes, 29 * 612 + 4, 10);
        Put(changedBytes, 29 * 612 + 104, 42);
        var baseline = LegacyShopProjection.DecodeAll(baselineBytes);
        var shops = LegacyShopProjection.DecodeAll(changedBytes);
        var overlay = LegacyShopProjection.Overlay(LegacyShopProjection.EncodePrefix(shops), baseline);
        Check(overlay[12].Item(1, 1) == 333 && overlay[12].Item(1, 2) == 222 && overlay[29].Quantity(0, 0) == 7, "7560-byte boundary includes secondID ofshop12group1 but not thirdID orshop29");
        var state = new GameState(OriginalRoster.Catalog)
        {
            Mode = 220,
            ModeStackDepth = 5,
            Money = -100,
            Year = 1450,
            Month = 3,
            Day = 14,
            WorldX = 4,
            WorldY = 4,
            PendingExperience = 17,
            ExperiencePending = 1,
            PendingBattle = 2,
            DungeonEntryPending = 1,
            PendingDungeonMap = "dungeon.amf"u8.ToArray(),
            PendingDungeonScript = "dungeon.scr"u8.ToArray(),
            DungeonFogDirty = 1,
            NewCharacterPending = 1,
            NewCharacterId = 3,
            VisualMode = 2,
            EndingSelection = 4,
            EndingResetFlag = 1,
            DayCounterStorage = -2,
            ShopNumber = 29,
            ScriptActive = 1
        };
        state.Globals[70] = unchecked((short)0x8080);
        state.Globals[999] = -123;
        state.Events[999] = -777;
        state.EventStatuses[199] = 4;
        byte[] partyImage = PartyCodec.Encode(state.Party);
        partyImage[^1] = 0xea;
        PartyCodec.Decode(partyImage, state.Party);
        state.QuestItemRecords[^1] = 0xdb;
        state.OwnedItems[199] = -9;
        state.AvailableItems[198] = 123;
        byte[] rawState = NativeGameStateCodec.Encode(state);
        Check(BinaryPrimitives.ReadInt32LittleEndian(rawState) == 30 && BinaryPrimitives.ReadInt32LittleEndian(rawState.AsSpan(4)) == -100, "own schema independent scalar count/money offset");
        // Independently add a real People record to the version1 binary schema, not through an inaccessible test-only setter.
        using (var peopleStream = new MemoryStream())
        {
            int peopleOffset = rawState.Length - 4;
            peopleStream.Write(rawState.AsSpan(0, peopleOffset));
            using (var writer = new BinaryWriter(peopleStream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(1);
                foreach (byte[] value in new[]
                {
                    "NPC"u8.ToArray(),
                    "npc"u8.ToArray(),
                    "talk"u8.ToArray()
                }
                )
                {
                    writer.Write(value.Length);
                    writer.Write(value);
                }
            }

            peopleStream.Write(rawState.AsSpan(peopleOffset + 4));
            state = NativeGameStateCodec.Decode(peopleStream.ToArray(), OriginalRoster.Catalog);
        }

        var areas = new WorldAreaStore();
        areas.ResetNames(messages);
        using (var amf = BinderArchive.Open(Path.Combine(resourceRoot, "AT2AMF.000")))
        {
            areas.Capture(0, WorldMapContext.Parse(amf.ReadMember(amf.FindResource("ground1.amf"u8))));
        }

        byte[] legacyBytes = new byte[LegacySaveImage.Size];
        "ATRIA-II SAVE FILE (c)Jamie system development\0"u8.CopyTo(legacyBytes);
        legacyBytes[90] = 0xc7;
        var template = LegacySaveImage.Parse(legacyBytes);
        var legacy = LegacySnapshotBridge.Capture(template, state, 300, areas, shops, metadata);
        Check(BinaryPrimitives.ReadInt32LittleEndian(legacy.Encode().AsSpan(233)) == 300 && legacy.Encode()[90] == 0xc7 && state.Mode == 220, "legacy saved underlying mode override and opaque template preservation");
        var load = LegacySnapshotBridge.Decode(legacy, baseline);
        using (var amf = BinderArchive.Open(Path.Combine(resourceRoot, "AT2AMF.000")))
        {
            var destination = load.PrepareDestination(name => amf.ReadMember(amf.FindResource(name.Span)));
            Check(destination.Mode == 300 && destination.AreaIndex == 0 && destination.MapMember.Span.SequenceEqual("ground1.amf"u8)
&& destination.World is not null, "legacy world activation resource identity frommetadata");
        }

        Check(LegacyLoadPlan.UnmappedLegacyFields.Count == 3, "legacy unresolved state is explicit rather than claimed complete");
        Reject(() => new NativeRuntimeContext("", 1, new byte[] { 1 }), "context schema required");
        Reject(() => new NativeRuntimeContext("session", 1, []), "nonempty runtime context required");
        var context = new NativeRuntimeContext("fixture-session", 1, new byte[] { 1, 2, 3 });
        var image = NativeSaveImage.Capture(state, 300, areas, shops, metadata, context, legacy);
        byte[] encoded = image.Encode();
        var decoded = NativeSaveImage.Parse(encoded, OriginalRoster.Catalog);
        Check(decoded.State.Mode == 300 && decoded.Shops[29].Quantity(0, 0) == 42 && decoded.Shops[12].Item(1, 2) == 444, "native envelope retains all30shops beyond legacy prefix");
        Check(decoded.State.PendingExperience == 17 && decoded.State.PendingBattle == 2 && decoded.State.DungeonFogDirty == 1
&& decoded.State.PendingDungeonMap.AsSpan().SequenceEqual("dungeon.amf"u8) && decoded.State.People.Count == 1 && decoded.State.People[0].Handler.Span.SequenceEqual("talk"u8), "native pending flags/resource refs/People retained");
        Check(decoded.State.BattleVariant == 0x8080 && decoded.State.Globals[999] == -123 && decoded.State.Events[999] == -777
&& decoded.State.EventStatuses[199] == 4 && PartyCodec.Encode(decoded.State.Party)[^1] == 0xea && decoded.State.QuestItemRecords[^1] == 0xdb && decoded.State.OwnedItems[199] == -9 && decoded.State.AvailableItems[198] == 123, "native arrays and WORD alias endpoints retained");
        Check(decoded.Context.Schema == "fixture-session" && decoded.Context.Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 })
&& decoded.LegacyTemplate is not null, "explicit context and optional legacy-template bytes retained without fabricating missing records");
        byte[] badMetadata = (byte[])encoded.Clone();
        Put(badMetadata, NativeSaveImage.HeaderSize + 117, 0);
        System.Security.Cryptography.SHA256.HashData(badMetadata.AsSpan(NativeSaveImage.HeaderSize)).CopyTo(badMetadata, 24);
        Reject(() => NativeSaveImage.Parse(badMetadata), "valid-checksum native image with empty slot metadata rejected");
        byte[] bad = (byte[])encoded.Clone();
        bad[^1] ^= 1;
        Reject(() => NativeSaveImage.Parse(bad), "checksum corruption rejected");
        bad = (byte[])encoded.Clone();
        Put(bad, 16, 2);
        Reject(() => NativeSaveImage.Parse(bad), "unsupported native version rejected");
        bad = (byte[])encoded.Clone();
        Put(bad, 20, -1);
        Reject(() => NativeSaveImage.Parse(bad), "length corruption rejected");
        var live = new GameState
        {
            Money = 999
        };
        var liveAreas = new WorldAreaStore();
        var liveShops = LegacyShopProjection.DecodeAll(baselineBytes);
        Reject(() => decoded.CommitStorage(live, liveAreas, liveShops, (_, _) => throw new InvalidDataException("Fixture rejected session context.")), "required context validation rejects before mutation");
        Check(live.Money == 999 && liveShops[29].Quantity(0, 0) == 7, "failed context preparation leaves all live storage untouched");
        decoded.CommitStorage(live, liveAreas, liveShops, (ctx, loaded) =>
        {
            if (ctx.Schema != "fixture-session" || ctx.Version != 1 || loaded.Mode != 300)
            {
                throw new InvalidDataException("Fixture context mismatch.");
            }
        });
        Check(live.Money == -100 && live.NewCharacterId == 3 && live.People.Count == 1 && liveShops[29].Quantity(0, 0) == 42, "full storage commit follows detached validation");
        state.Money = 88;
        Check(decoded.State.Money == -100, "snapshot owns detached state");
        MenuChecks(messages, metadata, empty, Check);
        RasterChecks(resourceRoot, messages, metadata, empty, Check);
        StoreChecks(resourceRoot, image, legacy, empty, Check);
        return "Persistence runtime checks passed (legacy7560-byte shop boundary, metadata, full native state/30shops/context, checksum/version, save/load input/countdown/raster and safe atomic store). Authentic legacy interoperability remains unverified.";
    }

    private static void Put(byte[] bytes, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value);
    private static void MenuChecks(IReadOnlyList<ReadOnlyMemory<byte>> messages, LegacySlotMetadata filled, LegacySlotMetadata empty, Action<bool, string> check)
    {
        var slots = Enumerable.Repeat(empty, 5).ToArray();
        slots[1] = filled;
        var menu = new SaveLoadMenuController();
        var commands = new List<SaveLoadMenuCommand>();
        menu.Enter(SaveLoadMenuKind.Save, 4, commands.Add);
        commands.Clear();
        menu.Input(new(Down: true, Accept: true), slots, commands.Add);
        check(menu.Selection == 4 && !menu.Confirmation && commands.Count == 0, "bounded down consumes lower-priority accept");
        menu.Input(new(Up: true, Accept: true), slots, commands.Add);
        check(menu.Selection == 3 && !menu.Confirmation, "up consumes accept");
        menu.Input(new(Accept: true), slots, commands.Add);
        check(menu.Confirmation && menu.ConfirmationChoice == 0, "save asks confirmation even empty slot");
        commands.Clear();
        menu.Input(new(Left: true), slots, commands.Add);
        check(commands.SequenceEqual(new[] { new SaveLoadMenuCommand(SaveLoadMenuEffect.Sound, 1) }), "confirmation left sounds even already selected");
        menu.Input(new(Right: true, Accept: true), slots, commands.Add);
        menu.Input(new(Accept: true), slots, commands.Add);
        check(!menu.Confirmation && menu.Countdown == 0, "No selection closes without save");
        menu.Input(new(Accept: true), slots, commands.Add);
        menu.Input(new(Accept: true), slots, commands.Add);
        check(menu.Countdown == 20, "native twenty-draw confirmation delay");
        for (int i = 20; i > 0; i--)
        {
            commands.Clear();
            menu.Input(new(Cancel: true), slots, commands.Add);
            check(commands.Count == 0, "countdown blocks all input");
            check(menu.AdvanceLogicalFrame().Countdown == i, "draw exposes pre-decrement countdown");
        }

        check(menu.PendingOperation, "operation deferred until after twentieth draw");
        commands.Clear();
        menu.Input(new(Down: true), slots, commands.Add);
        check(commands.SequenceEqual(new[] { new SaveLoadMenuCommand(SaveLoadMenuEffect.SaveSlot, 3), new(SaveLoadMenuEffect.RefreshSlots), new(SaveLoadMenuEffect.Sound, 1) })
&& menu.Selection == 4, "next input executes save then refresh before processing the same opportunity's navigation");
        menu.Enter(SaveLoadMenuKind.Load, 0, commands.Add);
        menu.Input(new(Accept: true), slots, commands.Add);
        check(menu.EmptyNotice && !menu.Confirmation, "load empty slot message");
        menu.Input(new(Down: true), slots, commands.Add);
        check(menu.Selection == 1 && menu.EmptyNotice, "native empty notice does not block slot navigation");
        menu.Input(new(Accept: true), slots, commands.Add);
        check(!menu.EmptyNotice && !menu.Confirmation, "accept first dismisses old empty notice");
        menu.Input(new(Accept: true), slots, commands.Add);
        check(menu.Confirmation, "present slot asks load confirmation");
        menu.Input(new(Cancel: true), slots, commands.Add);
        commands.Clear();
        menu.Input(new(Cancel: true), slots, commands.Add);
        check(commands.SequenceEqual(new[] { new SaveLoadMenuCommand(SaveLoadMenuEffect.Sound, 4), new(SaveLoadMenuEffect.PopMode), new(SaveLoadMenuEffect.RestoreBackground, 1) }), "cancel pop/redraw contract");
        menu.ReportFailure("fixture I/O failure");
        commands.Clear();
        menu.Input(new(Accept: true), slots, commands.Add);
        check(commands.Count == 0 && menu.Failure is not null, "safe I/O failure requires explicit host acknowledgement");
        menu.AcknowledgeFailure();
    }

    private static void RasterChecks(string root, IReadOnlyList<ReadOnlyMemory<byte>> messages, LegacySlotMetadata filled, LegacySlotMetadata empty, Action<bool, string> check)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        var renderer = new SaveLoadMenuRenderer(
            SpriteFile.Decode(archive.ReadMember(archive.FindResource("ETC"u8))),
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true),
            BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false),
            messages
        );
        var slots = Enumerable.Range(0, 5).Select(i => new SaveSlotInfo(i, i == 0 ? SaveSlotStatus.Native : SaveSlotStatus.Empty, i == 0 ? filled : empty, new(1450, 3, 14))).ToArray();
        var frame = new SaveLoadMenuFrame(SaveLoadMenuKind.Save, 0, false, 0, false, 0, 0);
        var plan = renderer.BuildPlan(frame, slots);
        check(plan.Count(p => p.Primitive == SaveMenuPrimitive.Panel) == 2
&& plan.Any(p => p.Primitive == SaveMenuPrimitive.Panel && p.X == 96 && p.Y == 64 && p.A == 12 && p.B == 7), "native static save panels");
        check(plan.Any(p => p.Text == GameText.Decode(messages[362].Span) && p.X == 156 && p.Y == 390 && p.Style == SaveMenuTextStyle.Selected), "save footer362 native coordinates");
        check(plan.Any(p => p.Text == GameText.Decode(filled.PrimaryName.Span).Trim().ToUpperInvariant() && p.X == 140 && p.Y == 99 && p.Style == SaveMenuTextStyle.Purple), "selected occupied slot native name position/palette");
        check(plan.Any(p => p.Text == GameText.Get("messages.361").Trim().ToUpperInvariant() && p.X == 140 && p.Y == 147), "empty second slot shares occupied-name alignment");
        plan = renderer.BuildPlan(frame with
        {
            Confirmation = true
        }, slots);
        check(plan.Any(p => p.Text == GameText.Decode(messages[312].Span) && p.X == 220 && p.Y == 160)
&& plan.Any(p => p.Text == GameText.Decode(messages[313].Span) && p.X == 260 && p.Y == 192 && p.Style == SaveMenuTextStyle.Selected), "native save confirmation title/yes");
        ushort[] pixels = new ushort[640 * 480];
        Array.Fill(pixels, (ushort)0x39e7);
        plan = renderer.BuildPlan(frame with
        {
            Countdown = 20
        }, slots);
        check(plan.Any(p => p.Primitive == SaveMenuPrimitive.Fill && p.X == 250 && p.Y == 200 && p.A == 1 && p.B == 13 && p.Color == 0xf81f), "native first progress bar inclusive one pixel width");
        renderer.Draw(plan, pixels);
        check(pixels[200 * 640 + 250] == 0xf81f, "real palette/sprite/font plan draws progress pixel");
        plan = renderer.BuildPlan(frame with
        {
            Countdown = 1
        }, slots);
        check(plan.Any(p => p.Primitive == SaveMenuPrimitive.Fill && p.A == 153), "last native countdown draw ends at153 pixels rather than invented full bar");
        var load = frame with
        {
            Kind = SaveLoadMenuKind.Load,
            EmptyNotice = true
        };
        plan = renderer.BuildPlan(load, slots);
        check(plan.Any(p => p.Text == GameText.Decode(messages[267].Span) && p.X == 192 && p.Y == 150), "native empty load notice267");
        renderer.Draw(plan, pixels);
        // Reproduce a fresh playtest-data directory: production enumeration supplies blank metadata.
        var blankSlots = Enumerable.Range(0, 5).Select(i => new SaveSlotInfo(i, SaveSlotStatus.Empty, LegacySlotMetadata.Empty([]), default)).ToArray();
        foreach (var kind in new[]
        {
            SaveLoadMenuKind.Save,
            SaveLoadMenuKind.Load
        }
        )
        {
            plan = renderer.BuildPlan(frame with
            {
                Kind = kind,
                Selection = 2
            }, blankSlots);
            for (int row = 0; row < 5; row++)
            {
                check(plan.Any(p => p.Primitive == SaveMenuPrimitive.Text && p.X == 140 && p.Y == 99 + 48 * row && p.Text == GameText.Get("messages.361").Trim().ToUpperInvariant()
&& p.Style == (row == 2 ? SaveMenuTextStyle.Purple : SaveMenuTextStyle.Normal)), "blank slot has localized visible label and selection palette");
            }

            ushort[] blank = new ushort[640 * 480], drawn = new ushort[640 * 480];
            renderer.Draw(plan.Where(p => p.Primitive != SaveMenuPrimitive.Text).ToArray(), blank);
            renderer.Draw(plan, drawn);
            for (int row = 0; row < 5; row++)
            {
                check(!blank.AsSpan((100 + 48 * row) * 640, 24 * 640).SequenceEqual(drawn.AsSpan((100 + 48 * row) * 640, 24 * 640)), "empty row draws actual bitmap glyph pixels");
            }

            check(blankSlots.All(s => s.Metadata.Present == 0 && s.Metadata.PrimaryName.IsEmpty), "presentation fix leaves empty metadata unchanged");
        }
    }

    private static void StoreChecks(string resourceRoot, NativeSaveImage image, LegacySaveImage legacy, LegacySlotMetadata empty, Action<bool, string> check)
    {
        string fixtureParent = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resourceRoot))!, "audit", "implementation", "save-runtime-tests");
        string fixture = Path.Combine(fixtureParent, Guid.NewGuid().ToString("N")), alias = Path.Combine(fixture, "alias");
        Directory.CreateDirectory(fixture);
        try
        {
            bool blocked = false;
            try
            {
                _ = new NativeSaveStore(resourceRoot, resourceRoot);
            }
            catch (InvalidOperationException)
            {
                blocked = true;
            }

            check(blocked, "actual original output root rejected before file creation");
            string data = Path.Combine(fixture, "data");
            var store = new NativeSaveStore(data, resourceRoot);
            store.WriteNative(0, image);
            check(store.ReadSlot(0)?.Native?.Shops[29].Quantity(0, 0) == 42, "atomic store writes full native bank");
            byte[] before = File.ReadAllBytes(store.GetSlotPath(0));
            using (var locked = new FileStream(store.GetSlotPath(0), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool failed = false;
                try
                {
                    store.WriteNative(0, image);
                }
                catch (IOException)
                {
                    failed = true;
                }
                catch (UnauthorizedAccessException)
                {
                    failed = true;
                }

                check(failed, "locked target replacement reports I/O failure");
            }

            check(File.ReadAllBytes(store.GetSlotPath(0)).AsSpan().SequenceEqual(before) && Directory.GetFiles(data, "*.tmp").Length == 0, "failed atomic replacement preserves old slot and cleans temporary file");
            store.WriteNative(0, image);
            store.WriteLegacy(1, legacy);
            var slots = store.InspectLibrary(false).OrderBy(slot => slot.Index).ToArray();
            check(slots[0].Status == SaveSlotStatus.Native && slots[1].Status == SaveSlotStatus.LegacyUnverified && slots.Length == 2, "native/legacy/missing slot inspection remain distinct");
            File.WriteAllBytes(store.GetSlotPath(2), new byte[] { 1, 2, 3 });
            check(store.InspectLibrary(false).Single(slot => slot.Index == 2).Status == SaveSlotStatus.Invalid, "invalid save is reported without modifying live state");
            string original = Path.Combine(fixture, "original");
            Directory.CreateDirectory(original);
            string sentinel = Path.Combine(original, "immutable.bin");
            File.WriteAllBytes(sentinel, new byte[] { 1, 2, 3, 4 });
            // A workspace-owned junction tests resolved ancestors, including a nonexistent descendant. No original resources are targeted.
            if (OperatingSystem.IsWindows())
            {
                if (alias.IndexOfAny("&|<>^!%\"\r\n".ToCharArray()) >= 0 || original.IndexOfAny("&|<>^!%\"\r\n".ToCharArray()) >= 0)
                {
                    throw new InvalidOperationException("Junction fixture path contains shell metacharacters.");
                }

                var start = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (string arg in new[]
                {
                    "/d",
                    "/c",
                    "mklink",
                    "/J",
                    alias,
                    original
                }
                )
                {
                    start.ArgumentList.Add(arg);
                }

                using var process = Process.Start(start) ?? throw new InvalidOperationException("Junction fixture did not start.");
                string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(true);
                    throw new InvalidOperationException("Junction fixture timed out.");
                }

                check(process.ExitCode == 0, "junction fixture created: " + stdout + stderr);
                blocked = false;
                try
                {
                    _ = new NativeSaveStore(Path.Combine(alias, "child"), original);
                }
                catch (InvalidOperationException)
                {
                    blocked = true;
                }

                check(blocked && !Directory.Exists(Path.Combine(original, "child"))
&& File.ReadAllBytes(sentinel).AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4 }), "junction/ancestor alias into immutable tree rejected without mutation");
            }
        }
        finally
        {
            if (Directory.Exists(alias))
            {
                Directory.Delete(alias); // Remove only the link itself before bounded recursive fixture cleanup.
            }

            string resolved = Path.GetFullPath(fixture), allowed = Path.GetFullPath(fixtureParent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Fixture cleanup escaped its owned directory.");
            }

            Directory.Delete(resolved, true);
        }
    }
}
