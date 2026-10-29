using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Game.World;

namespace Atria.Game.Persistence;

public sealed class NativeRuntimeContext
{
    public string Schema
    {
        get;
    }
    public int Version
    {
        get;
    }

    private readonly byte[] payload;
    public ReadOnlyMemory<byte> Payload => payload;

    public NativeRuntimeContext(string schema, int version, ReadOnlySpan<byte> payload)
    {
        if (string.IsNullOrWhiteSpace(schema) || schema.Length > 64 || schema.Any(c => c < 33 || c > 126) || version < 1
|| payload.Length is < 1 or > 1048576)
        {
            throw new InvalidDataException("A bounded, versioned native runtime context is required.");
        }

        Schema = schema;
        Version = version;
        this.payload = payload.ToArray();
    }
}

/// <summary>Own native format, distinct from original saves. Full state/areas/30 shops plus required versioned session context.</summary>
public sealed class NativeSaveImage
{
    public const int Version = 1, HeaderSize = 56, MaximumSize = 2097152;
    private static ReadOnlySpan<byte> Magic => "ATRIA-NATIVE-SV\0"u8;
    public GameState State
    {
        get;
    }
    public WorldAreaStore Areas
    {
        get;
    }
    public ShopStock[] Shops
    {
        get;
    }
    public LegacySlotMetadata Metadata
    {
        get;
    }
    public NativeRuntimeContext Context
    {
        get;
    }
    public LegacySaveImage? LegacyTemplate
    {
        get;
    }

    private NativeSaveImage(
        GameState state,
        WorldAreaStore areas,
        ShopStock[] shops,
        LegacySlotMetadata metadata,
        NativeRuntimeContext context,
        LegacySaveImage? legacyTemplate
    )
    {
        State = state;
        Areas = areas;
        Shops = shops;
        Metadata = metadata;
        Context = context;
        LegacyTemplate = legacyTemplate;
    }

    public static NativeSaveImage Capture(
        GameState state,
        int savedMode,
        WorldAreaStore areas,
        IReadOnlyList<ShopStock> shops,
        LegacySlotMetadata metadata,
        NativeRuntimeContext context,
        LegacySaveImage? legacyTemplate = null
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        if (metadata.Present != 1)
        {
            throw new InvalidDataException("Native snapshot requires populated slot metadata.");
        }

        var copy = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state), state.HeroCatalog);
        copy.Mode = savedMode;
        var ownedAreas = new WorldAreaStore();
        for (int i = 0; i < 8; i++)
        {
            ownedAreas.Import(i, areas.GetInline(i), areas.GetPayload(i));
        }

        var ownedShops = LegacyShopProjection.DecodeAll(LegacyShopProjection.EncodeAll(shops));
        return new(
            copy,
            ownedAreas,
            ownedShops,
            LegacySlotMetadata.Parse(metadata.Encode()),
            new(context.Schema, context.Version, context.Payload.Span),
            legacyTemplate
        );
    }

    public static bool HasNativeSignature(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Magic);
    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Metadata.Encode());
        NativeGameStateCodec.WriteBytes(writer, NativeGameStateCodec.Encode(State), NativeGameStateCodec.MaxBytes);
        for (int i = 0; i < 8; i++)
        {
            writer.Write(Areas.GetInline(i));
            writer.Write(Areas.GetPayload(i));
        }

        writer.Write(LegacyShopProjection.EncodeAll(Shops));
        NativeGameStateCodec.WriteBytes(writer, Encoding.ASCII.GetBytes(Context.Schema), 64);
        writer.Write(Context.Version);
        NativeGameStateCodec.WriteBytes(writer, Context.Payload.Span, 1048576);
        NativeGameStateCodec.WriteBytes(writer, LegacyTemplate?.Encode() ?? [], LegacySaveImage.Size);
        writer.Flush();
        byte[] payload = stream.ToArray();
        if (payload.Length > MaximumSize - HeaderSize)
        {
            throw new InvalidDataException("Native snapshot exceeds bounded container capacity.");
        }

        byte[] result = new byte[HeaderSize + payload.Length];
        Magic.CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), Version);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20), payload.Length);
        SHA256.HashData(payload).CopyTo(result, 24);
        payload.CopyTo(result, HeaderSize);
        return result;
    }

    public static NativeSaveImage Parse(ReadOnlySpan<byte> bytes, HeroCatalog? heroCatalog = null)
    {
        if (bytes.Length is < HeaderSize or > MaximumSize || !bytes.StartsWith(Magic))
        {
            throw new InvalidDataException("Native save header/signature is invalid.");
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]) != Version)
        {
            throw new InvalidDataException("Native save version is unsupported.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]);
        if (count != bytes.Length - HeaderSize)
        {
            throw new InvalidDataException("Native save payload length is invalid.");
        }

        var payload = bytes[HeaderSize..];
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), bytes.Slice(24, 32)))
        {
            throw new InvalidDataException("Native save checksum is invalid.");
        }

        using var stream = new MemoryStream(payload.ToArray(), false);
        using var reader = new BinaryReader(stream);
        try
        {
            var metadata = LegacySlotMetadata.Parse(NativeGameStateCodec.ReadExact(reader, 133));
            _ = metadata.PrimaryName;
            _ = metadata.SecondaryName;
            if (metadata.Present != 1)
            {
                throw new InvalidDataException("Native save slot metadata must be populated.");
            }

            var state = NativeGameStateCodec.Decode(NativeGameStateCodec.ReadBytes(reader, NativeGameStateCodec.MaxBytes), heroCatalog);
            var areas = new WorldAreaStore();
            for (int i = 0; i < 8; i++)
            {
                areas.Import(i, NativeGameStateCodec.ReadExact(reader, 417), NativeGameStateCodec.ReadExact(reader, 32800));
            }

            var shops = LegacyShopProjection.DecodeAll(NativeGameStateCodec.ReadExact(reader, LegacyShopProjection.FullBytes));
            byte[] schemaBytes = NativeGameStateCodec.ReadBytes(reader, 64);
            foreach (byte value in schemaBytes)
            {
                if (value is < 33 or > 126)
                {
                    throw new InvalidDataException("Native context schema must be printable ASCII.");
                }
            }

            var schema = Encoding.ASCII.GetString(schemaBytes);
            int contextVersion = reader.ReadInt32();
            var context = new NativeRuntimeContext(schema, contextVersion, NativeGameStateCodec.ReadBytes(reader, 1048576));
            byte[] legacy = NativeGameStateCodec.ReadBytes(reader, LegacySaveImage.Size);
            var template = legacy.Length == 0 ? null : LegacySaveImage.Parse(legacy);
            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Native snapshot contains trailing unclassified bytes.");
            }

            return new(state, areas, shops, metadata, context, template);
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException("Native snapshot body is truncated.", error);
        }
    }

    /// <summary>Call only after caller has validated Context schema and prepared all replacement resources/state.</summary>
    public void CommitStorage(GameState target, WorldAreaStore targetAreas, ShopStock[] targetShops, Action<NativeRuntimeContext, GameState> validateAndPrepareRuntime)
    {
        if (targetShops.Length != 30)
        {
            throw new InvalidDataException("Native shop target must contain30 records.");
        }
        // Reparse to validate/detach potentially modified snapshot properties before any live write.
        ArgumentNullException.ThrowIfNull(validateAndPrepareRuntime);
        var prepared = Parse(Encode());
        validateAndPrepareRuntime(prepared.Context, prepared.State);
        var inline = new byte[8][];
        var payload = new byte[8][];
        for (int i = 0; i < 8; i++)
        {
            inline[i] = prepared.Areas.GetInline(i);
            payload[i] = prepared.Areas.GetPayload(i);
        }

        NativeGameStateCodec.Apply(prepared.State, target);
        for (int i = 0; i < 8; i++)
        {
            targetAreas.Import(i, inline[i], payload[i]);
        }

        for (int i = 0; i < 30; i++)
        {
            targetShops[i] = prepared.Shops[i];
        }
    }
}
