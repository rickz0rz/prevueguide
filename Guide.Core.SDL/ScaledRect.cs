namespace Guide.Core.SDL;

public struct ScaledRect
{
    public int Scale;

    public int X;

    public int Y;

    public int W;

    public int H;

    public SDL3.SDL.Rect ToRect()
    {
        return new SDL3.SDL.Rect {  X = X * Scale, Y = Y * Scale, W = W * Scale, H = H * Scale };
    }
}
