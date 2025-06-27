namespace PrevueGuide.Core.SDL;

public struct ScaledFRect
{
    public int Scale;

    public float X;

    public float Y;

    public float W;

    public float H;

    public SDL3.SDL.FRect ToFRect()
    {
        return new SDL3.SDL.FRect {  X = X * Scale, Y = Y * Scale, W = W * Scale, H = H * Scale };
    }
}
