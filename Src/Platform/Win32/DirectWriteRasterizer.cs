using Atria.Render.UI;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Atria.Platform.NativeCom;

namespace Atria.Platform.Win32;



/// <summary>Windows shaping/fallback rendered to an owned grayscale mask; no GPU or window dependency.</summary>
public sealed unsafe partial class DirectWriteRasterizer : Atria.Render.UI.ITextRasterizer
{
    private const int NoInterface = unchecked((int)0x80004002);
    private const int NotImplemented = unchecked((int)0x80004001);
    private const int Padding = 4;
    private const int MaximumLayoutWidth = 4096;
    private const int MaximumLayoutHeight = 1024;
    private const uint FontWeightNormal = 400, FontWeightBold = 700, FontStyleNormal = 0, FontStretchNormal = 5;
    private const uint BitmapObject = 7, Blackness = 0x00000042, White = 0x00ffffff;
    private static readonly Guid factoryId = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private static readonly Guid rendererId = new("ef8a8135-5cc6-45fe-8825-c5a0724eb819");
    private static readonly Guid snappingId = new("eaf3a2da-ecf4-4d24-b644-b34f6842024b");
    private static readonly Guid unknownId = new("00000000-0000-0000-c000-000000000046");
    private static readonly nint callbacks = CreateCallbacks();
    private nint factory, interop, renderingParameters;

    public DirectWriteRasterizer()
    {
        try
        {
            Guid id = factoryId;
            nint created;
            Check(DWriteCreateFactory(0, &id, &created), nameof(DWriteCreateFactory));
            factory = created;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Method(factory, FactorySlots.GetGdiInterop))(factory, &created), "GetGdiInterop");
            interop = created;
            const float gamma = 2.2f, contrast = 1, grayscale = 0;
            const uint flatPixels = 0, naturalRendering = 4;
            Check(((delegate* unmanaged[Stdcall]<nint, float, float, float, uint, uint, nint*, int>)Method(factory, FactorySlots.CreateRenderingParameters))
                (factory, gamma, contrast, grayscale, flatPixels, naturalRendering, &created), "CreateRenderingParameters");
            renderingParameters = created;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public TextMask Render(string text, float size, string family = "Segoe UI", bool bold = false, int wrapWidth = MaximumLayoutWidth)
    {
        ObjectDisposedException.ThrowIf(factory == 0, this);
        if (!float.IsFinite(size) || size is < 1 or > 128 || wrapWidth is < 1 or > MaximumLayoutWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        nint format = 0, layout = 0, target = 0;
        try
        {
            fixed (char* fontName = family, locale = "ru-RU", value = text)
            {
                Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, uint, uint, uint, float, char*, nint*, int>)Method(factory, FactorySlots.CreateTextFormat))
                    (factory, fontName, 0, bold ? FontWeightBold : FontWeightNormal, FontStyleNormal, FontStretchNormal, size, locale, &format), "CreateTextFormat");
                if (IsRightToLeft(text))
                {
                    const uint rightToLeft = 1;
                    Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Method(format, FormatSlots.SetReadingDirection))(format, rightToLeft), "SetReadingDirection");
                }

                Check(((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, float, float, nint*, int>)Method(factory, FactorySlots.CreateTextLayout))
                    (factory, value, (uint)text.Length, format, wrapWidth, MaximumLayoutHeight, &layout), "CreateTextLayout");
            }

            TextMetrics metrics;
            Check(((delegate* unmanaged[Stdcall]<nint, TextMetrics*, int>)Method(layout, LayoutSlots.GetMetrics))(layout, &metrics), "GetTextMetrics");
            int advance = (int)Math.Ceiling(metrics.WidthIncludingTrailingWhitespace);
            int width = Math.Clamp(advance + Padding * 2, 1, MaximumLayoutWidth + Padding * 2);
            int height = Math.Clamp((int)Math.Ceiling(metrics.Height) + Padding * 2, 1, MaximumLayoutHeight + Padding * 2);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, nint*, int>)Method(interop, GdiSlots.CreateBitmapRenderTarget))
                (interop, 0, (uint)width, (uint)height, &target), "CreateBitmapRenderTarget");
            nint dc = ((delegate* unmanaged[Stdcall]<nint, nint>)Method(target, BitmapSlots.GetMemoryDc))(target);
            if (!PatBlt(dc, 0, 0, width, height, Blackness))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            // Draw is synchronous. The callback object and its target live until it returns.
            CallbackState renderer = new() { Vtable = callbacks, Target = target, Parameters = renderingParameters };
            Check(((delegate* unmanaged[Stdcall]<nint, nint, CallbackState*, float, float, int>)Method(layout, LayoutSlots.Draw))
                (layout, 0, &renderer, Padding - metrics.Left, Padding), "DrawTextLayout");
            GdiFlush();
            DibSection bitmap;
            if (GetObjectW(GetCurrentObject(dc, BitmapObject), sizeof(DibSection), &bitmap) != sizeof(DibSection) || bitmap.Bitmap.Bits == null || bitmap.Bitmap.BitsPerPixel != 32)
            {
                throw new InvalidOperationException("DirectWrite did not supply a 32-bit DIB surface.");
            }

            byte[] coverage = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                // GetMemoryDC guarantees a top-down DIB; GetObject normalizes its height sign.
                byte* row = (byte*)bitmap.Bitmap.Bits + y * bitmap.Bitmap.Stride;
                for (int x = 0; x < width; x++)
                {
                    coverage[y * width + x] = Math.Max(row[x * 4], Math.Max(row[x * 4 + 1], row[x * 4 + 2]));
                }
            }

            return new(width, height, advance, coverage, renderer.MissingGlyphs, (int)metrics.LineCount);
        }
        finally
        {
            Release(ref target);
            Release(ref layout);
            Release(ref format);
        }
    }

    public static int MaskPadding => Padding;

    public void Dispose()
    {
        Release(ref renderingParameters);
        Release(ref interop);
        Release(ref factory);
    }

    private static bool IsRightToLeft(string text)
    {
        const uint characterDirection = 2;
        const ushort leftToRight = 1, rightToLeft = 2;
        var types = new ushort[text.Length];
        fixed (char* value = text)
        fixed (ushort* classifications = types)
        {
            if (!GetStringTypeW(characterDirection, value, text.Length, classifications))
            {
                return false;
            }
        }
        foreach (ushort direction in types)
        {
            if (direction is leftToRight or rightToLeft)
            {
                return direction == rightToLeft;
            }
        }
        return false;
    }

    private static nint CreateCallbacks()
    {
        // One immutable COM vtable for the lifetime of the process, shared by synchronous draw calls.
        nint* methods = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(DirectWriteRasterizer), 10 * sizeof(nint));
        methods[0] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, Guid*, nint*, int>)&QueryInterface;
        methods[1] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, uint>)&Retain;
        methods[2] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, uint>)&Retain;
        methods[3] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, nint, int*, int>)&DisableSnapping;
        methods[4] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, nint, Matrix*, int>)&GetTransform;
        methods[5] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, nint, float*, int>)&GetPixelsPerDip;
        methods[6] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, nint, float, float, uint, GlyphRun*, nint, nint, int>)&DrawGlyphRun;
        methods[7] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, nint, float, float, nint, nint, int>)&UnsupportedDecoration;
        methods[8] = methods[7];
        methods[9] = (nint)(delegate* unmanaged[Stdcall]<CallbackState*, nint, float, float, nint, int, int, nint, int>)&UnsupportedInlineObject;
        return (nint)methods;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(CallbackState* self, Guid* id, nint* result)
    {
        *result = 0;
        if (*id != unknownId && *id != rendererId && *id != snappingId)
        {
            return NoInterface;
        }
        *result = (nint)self;
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Retain(CallbackState* self) => 1;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DisableSnapping(CallbackState* self, nint context, int* disabled) {
        *disabled = 1;
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetTransform(CallbackState* self, nint context, Matrix* value) {
        *value = new() { M11 = 1, M22 = 1 };
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetPixelsPerDip(CallbackState* self, nint context, float* value) {
        *value = 1;
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DrawGlyphRun(CallbackState* self, nint context, float x, float y, uint mode, GlyphRun* run, nint description, nint effect)
    {
        for (uint i = 0; i < run->Count; i++)
        {
            if (run->Indices[i] == 0)
            {
                self->MissingGlyphs++;
            }
        }
        return ((delegate* unmanaged[Stdcall]<nint, float, float, uint, GlyphRun*, nint, uint, nint, int>)Method(self->Target, BitmapSlots.DrawGlyphRun))
            (self->Target, x, y, mode, run, self->Parameters, White, 0);
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int UnsupportedDecoration(CallbackState* self, nint context, float x, float y, nint decoration, nint effect) => NotImplemented;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int UnsupportedInlineObject(CallbackState* self, nint context, float x, float y, nint inline, int sideways, int rtl, nint effect) => NotImplemented;

    // Windows SDK dwrite.h slots; keep ABI details at this boundary.
    private static class FactorySlots
    {
        public const int CreateRenderingParameters = 12, CreateTextFormat = 15, GetGdiInterop = 17, CreateTextLayout = 18;
    }
    private static class FormatSlots
    {
        public const int SetReadingDirection = 6;
    }
    private static class LayoutSlots
    {
        public const int Draw = 58, GetMetrics = 60;
    }
    private static class GdiSlots
    {
        public const int CreateBitmapRenderTarget = 7;
    }
    private static class BitmapSlots
    {
        public const int DrawGlyphRun = 3, GetMemoryDc = 4;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CallbackState
    {
        public nint Vtable, Target, Parameters;
        public int MissingGlyphs;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix
    {
        public float M11, M12, M21, M22, Dx, Dy;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphRun
    {
        public nint FontFace;
        public float Size;
        public uint Count;
        public ushort* Indices;
        public nint Advances, Offsets;
        public int Sideways;
        public uint BidiLevel;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TextMetrics
    {
        public float Left, Top, Width, WidthIncludingTrailingWhitespace, Height, LayoutWidth, LayoutHeight;
        public uint BidiDepth, LineCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type, Width, Height, Stride;
        public ushort Planes, BitsPerPixel;
        public void* Bits;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitsPerPixel;
        public uint Compression, ImageSize;
        public int XResolution, YResolution;
        public uint ColorsUsed, ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DibSection
    {
        public Bitmap Bitmap;
        public BitmapHeader Header;
        public uint RedMask, GreenMask, BlueMask;
        public nint Section;
        public uint Offset;
    }

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(uint type, Guid* id, nint* factory);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PatBlt(nint dc, int x, int y, int width, int height, uint operation);
    [LibraryImport("gdi32.dll")]
    private static partial nint GetCurrentObject(nint dc, uint type);
    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint handle, int size, void* output);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GdiFlush();
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetStringTypeW(uint type, char* text, int length, ushort* output);
}
