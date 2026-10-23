using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Persistence;

/// <summary>Owned, bounded legacy image. Static recovered layout; authentic runtime parity pending.</summary>
public sealed class LegacySaveImage
{
    public const int Size = 288671, AreaCount = 8, AreaInlineSize = 417, AreaPayloadSize = 32800;
    public const int AreaOffset = 8555, AreaStride = AreaInlineSize + AreaPayloadSize;
    private static ReadOnlySpan<byte> Signature => "ATRIA-II SAVE FILE (c)Jamie system development\0"u8;
    private readonly byte[] bytes;
    private LegacySaveImage(byte[] bytes) => this.bytes = bytes;

    public static LegacySaveImage Parse(ReadOnlySpan<byte> input)
    {
        if (input.Length != Size) throw new InvalidDataException($"Legacy save must contain exactly {Size} bytes.");
        // Original loader omitted this check. Native import deliberately requires writer identity.
        if (!input.StartsWith(Signature)) throw new InvalidDataException("Legacy save signature is invalid.");
        return new(input.ToArray());
    }
    public byte[] Encode() => (byte[])bytes.Clone();
    public byte[] GetHeader() => bytes.AsSpan(0, 100).ToArray();
    public byte[] GetSlotMetadata() => bytes.AsSpan(100, 133).ToArray();
    public byte[] GetAreaInline(int index) => bytes.AsSpan(AreaPosition(index), AreaInlineSize).ToArray();
    public byte[] GetAreaPayload(int index) => bytes.AsSpan(AreaPosition(index) + AreaInlineSize, AreaPayloadSize).ToArray();
    private static int AreaPosition(int index) => (uint)index < AreaCount ? AreaOffset + index * AreaStride : throw new ArgumentOutOfRangeException(nameof(index));
    private int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    private void Put32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    /// <summary>Creates a detached storage projection, not an activated playable context.
    /// Unserialized transient fields remain zero; resource/context reconstruction is still required.</summary>
    public GameState ReadRecoveredState()
    {
        var state = new GameState
        {
            Mode = I32(233), WorldIndex = I32(237), WorldX = I32(241), WorldY = I32(245),
            ResourceCounterDc88 = I32(357), InitializedPartyCount = I32(8539),
            Money = I32(8543) ^ 0xff, Score = I32(8547), ResourceCounterFc9a = I32(8551),
            EventCounter = I32(279091), Year = I32(288655), Month = I32(288659), Day = I32(288663),
            DayCounterStorage = I32(288667)
        };
        bytes.AsSpan(361, 2800).CopyTo(state.PartyRecords);
        bytes.AsSpan(4761, 3774).CopyTo(state.QuestItemRecords);
        ReadInts(3161, state.OwnedItems); ReadInts(3961, state.AvailableItems);
        ReadInts(274291, state.Events); ReadInts(278291, state.EventStatuses);
        for (int i = 0; i < state.Globals.Length; i++) state.Globals[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(279095 + i * 2, 2));
        return state;
    }
    /// <summary>Returns a new image with only proven state fields replaced. Opaque blocks and metadata survive.
    /// Does not perform original pre-save area synchronization or metadata refresh.</summary>
    public LegacySaveImage WithRecoveredState(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var image = new LegacySaveImage(Encode());
        image.Put32(233, state.Mode); image.Put32(237, state.WorldIndex); image.Put32(241, state.WorldX); image.Put32(245, state.WorldY);
        image.Put32(357, state.ResourceCounterDc88); image.Put32(8539, state.InitializedPartyCount);
        image.Put32(8543, state.Money ^ 0xff); image.Put32(8547, state.Score); image.Put32(8551, state.ResourceCounterFc9a);
        image.Put32(279091, state.EventCounter); image.Put32(288655, state.Year); image.Put32(288659, state.Month); image.Put32(288663, state.Day); image.Put32(288667, state.DayCounterStorage);
        state.PartyRecords.CopyTo(image.bytes, 361); state.QuestItemRecords.CopyTo(image.bytes, 4761);
        image.WriteInts(3161, state.OwnedItems); image.WriteInts(3961, state.AvailableItems);
        image.WriteInts(274291, state.Events); image.WriteInts(278291, state.EventStatuses);
        for (int i = 0; i < state.Globals.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(image.bytes.AsSpan(279095 + i * 2, 2), state.Globals[i]);
        return image;
    }
    // Serialized block46dc24..46dc87: settings agent recovered only its first three bytes.
    public byte[] GetSettingsContext()=>bytes.AsSpan(257,100).ToArray();
    public byte[] GetUnmappedPrefix()=>bytes.AsSpan(249,8).ToArray();
    public byte[] GetQuestTail()=>bytes.AsSpan(8535,4).ToArray();
    public Atria.Game.Settings.NativeSettingsState ReadSettings()=>Atria.Game.Settings.NativeSettingsState.Parse([1,bytes[257],bytes[258],bytes[259]]);
    public LegacySaveImage WithSettings(Atria.Game.Settings.NativeSettingsState settings)
    {
        ArgumentNullException.ThrowIfNull(settings);var copy=new LegacySaveImage(Encode());
        settings.Encode().AsSpan(1,3).CopyTo(copy.bytes.AsSpan(257,3));return copy;
    }
    public byte[] GetShopPrefix()=>bytes.AsSpan(281095,7560).ToArray();
    public LegacySaveImage WithSlotMetadata(LegacySlotMetadata metadata)
    {var copy=new LegacySaveImage(Encode());metadata.Encode().CopyTo(copy.bytes,100);return copy;}
    public LegacySaveImage WithSavedMode(int mode){var copy=new LegacySaveImage(Encode());copy.Put32(233,mode);return copy;}
    public LegacySaveImage WithArea(int index,ReadOnlySpan<byte> inline,ReadOnlySpan<byte> payload)
    {
        if(inline.Length!=AreaInlineSize||payload.Length!=AreaPayloadSize)throw new InvalidDataException("Legacy area block length is invalid.");
        int at=AreaPosition(index);var copy=new LegacySaveImage(Encode());inline.CopyTo(copy.bytes.AsSpan(at));payload.CopyTo(copy.bytes.AsSpan(at+AreaInlineSize));return copy;
    }
    public LegacySaveImage WithShopPrefix(ReadOnlySpan<byte> prefix)
    {if(prefix.Length!=7560)throw new InvalidDataException("Legacy shop prefix must contain7560 bytes.");var copy=new LegacySaveImage(Encode());prefix.CopyTo(copy.bytes.AsSpan(281095));return copy;}
    private void ReadInts(int offset, int[] values) { for (int i = 0; i < values.Length; i++) values[i] = I32(offset + i * 4); }
    private void WriteInts(int offset, int[] values) { for (int i = 0; i < values.Length; i++) Put32(offset + i * 4, values[i]); }
}

