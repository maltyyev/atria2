namespace Tests.Core.Render;

/// <summary>Writes 640-by-480 RGB565 test buffers as top-down BMP files.</summary>
public static class BitmapTestWriter
{
    public static void Save(string path, ushort[] pixels)
    {
        const int width = 640, height = 480, headerBytes = 54, bytesPerPixel = 3;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4d42);
        writer.Write(headerBytes + width * height * bytesPerPixel);
        writer.Write(0);
        writer.Write(headerBytes);
        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(new byte[24]);
        foreach (ushort pixel in pixels)
        {
            writer.Write((byte)((pixel & 31) * 255 / 31));
            writer.Write((byte)(((pixel >> 5) & 63) * 255 / 63));
            writer.Write((byte)((pixel >> 11) * 255 / 31));
        }
    }
}
