namespace Guide.Core.SDL;

public struct ScaledFPoint
{
    public float X;

    public float Y;

    public SDL3.SDL.FPoint ToFPoint()
    {
        return new SDL3.SDL.FPoint { X = X * Configuration.Scale, Y = Y * Configuration.Scale };
    }
}
