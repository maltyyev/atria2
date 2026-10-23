using Atria.Render.Codecs;

namespace Atria.Render.UI;

public static class UiRaster
{
    public static void DrawPanel(SpriteFile tiles, int x, int y, int horizontalTiles, int verticalTiles,
        Span<ushort> target, int width = 640, int height = 480, int stride = 640, bool arrows = false)
    {
        if (horizontalTiles < 0 || verticalTiles < 0) throw new ArgumentOutOfRangeException(nameof(horizontalTiles));
        if (tiles.Frames.Length < (arrows ? 12 : 8)) throw new InvalidDataException("ETC panel tiles absent.");
        int w = checked((horizontalTiles + 2) * 32), h = checked((verticalTiles + 2) * 32);
        // 0x426e10/428a80 ->428790 corners,428870 edges, optional428980 arrows.
        Draw(tiles,0,target,width,height,stride,x+2,y+4); Draw(tiles,1,target,width,height,stride,x+w-32,y+4);
        Draw(tiles,2,target,width,height,stride,x+2,y+h-32); Draw(tiles,3,target,width,height,stride,x+w-32,y+h-32);
        for (int i = 1; i <= horizontalTiles; i++)
        { Draw(tiles,4,target,width,height,stride,x+i*32,y+4); Draw(tiles,5,target,width,height,stride,x+i*32,y+h-14); }
        for (int i = 1; i <= verticalTiles; i++)
        { Draw(tiles,6,target,width,height,stride,x+4,y+i*32); Draw(tiles,7,target,width,height,stride,x+w-14,y+i*32); }
        if (!arrows) return;
        int middleX = x + ((horizontalTiles+2)>>1)*32, middleY = y + ((verticalTiles+2)>>1)*32;
        Draw(tiles,8,target,width,height,stride,middleX,y+2); Draw(tiles,9,target,width,height,stride,middleX,y+h-17);
        Draw(tiles,10,target,width,height,stride,x+2,middleY); Draw(tiles,11,target,width,height,stride,x+w-17,middleY);
    }

    private static void Draw(SpriteFile tiles,int frame,Span<ushort> target,int width,int height,int stride,int x,int y) =>
        SpriteRaster.Draw565(tiles.Frames[frame],target,width,height,stride,x,y,SpriteBlend.Copy);

    public static void TintHalf(Span<ushort> target,int width,int height,int stride,int x,int y,int boxWidth,int boxHeight,ushort tint)
    {
        Validate(target,width,height,stride);
        if (boxWidth < 0 || boxHeight < 0) throw new ArgumentOutOfRangeException(nameof(boxWidth));
        int right = (int)Math.Min(width,(long)x+boxWidth), bottom = (int)Math.Min(height,(long)y+boxHeight);
        for (int row = Math.Max(0,y); row < bottom; row++)
            for (int column = Math.Max(0,x); column < right; column++)
            {
                int at = row*stride+column;
                target[at] = (ushort)(((target[at]&0xf7de)+(tint&0xf7de))>>1);
            }
    }

    //0x425830 ->425790/4257e0 ->4253a0/4254b0. Phase is native48e3ac, normally0.
    public static int DrawStyledText(BitmapFont font,ReadOnlySpan<byte> text,ReadOnlySpan<ushort> palette,int phase,
        Span<ushort> target,int width,int height,int stride,int x,int y)
    {
        Validate(target,width,height,stride);
        if (palette.Length != 32 || (uint)phase >= 32) throw new ArgumentException("Style requires32 colors and phase0..31.");
        int count = 0;
        foreach(byte character in text)
        {
            if(character == 0) break;
            ReadOnlySpan<byte> glyph = font.GetGlyph(character);
            int paletteIndex = phase;
            for(int row = 0; row < font.Height; row++)
            {
                if(row > (font.Height == 16 ? 2 : 5)) paletteIndex = (paletteIndex+2)&31;
                long dy = (long)y+row;
                if(dy < 0 || dy >= height) continue;
                for(int column = 0; column < font.Width; column++)
                {
                    long dx = (long)x+(long)count*font.Advance+column;
                    if(dx >= 0 && dx < width && (glyph[row*(font.Width/8)+column/8] & (0x80>>(column&7))) != 0)
                        target[(int)dy*stride+(int)dx] = palette[paletteIndex];
                }
            }
            count++;
        }
        return checked(count*font.Advance);
    }
    private static void Validate(Span<ushort> target,int width,int height,int stride)
    {
        if(width < 0 || height < 0 || stride < width || (long)stride*height > target.Length)
            throw new ArgumentException("Invalid UI raster dimensions.");
    }
}
