namespace Atria.Render;

public readonly record struct Viewport(int X,int Y,int Width,int Height)
{
    public static Viewport Fit(int width,int height,int sourceWidth=640,int sourceHeight=480)
    {
        if(sourceWidth<=0 || sourceHeight<=0) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if(width<=0 || height<=0) return default;
        double scale=Math.Min((double)width/sourceWidth,(double)height/sourceHeight);
        int w=(int)Math.Floor(sourceWidth*scale),h=(int)Math.Floor(sourceHeight*scale);
        return new((width-w)/2,(height-h)/2,w,h);
    }
    public bool TryMapInput(int x,int y,out int logicalX,out int logicalY)
    {
        logicalX=logicalY=0;
        if(Width<=0 || Height<=0 || x<X || y<Y || x>=X+Width || y>=Y+Height) return false;
        logicalX=(int)((long)(x-X)*640/Width); logicalY=(int)((long)(y-Y)*480/Height);
        return true;
    }
}
