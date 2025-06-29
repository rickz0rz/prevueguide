namespace PrevueGuide.Core.SDL;

public struct ScaledFPoint
{
    public int Scale;

    public float X;

    public float Y;

    public SDL3.SDL.FPoint ToFPoint()
    {
        return new SDL3.SDL.FPoint { X = X * Scale, Y = Y * Scale };
    }
}
