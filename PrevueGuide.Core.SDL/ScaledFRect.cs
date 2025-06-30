namespace PrevueGuide.Core.SDL;

public struct ScaledFRect
{
    public float X;

    public float Y;

    public float W;

    public float H;

    public SDL3.SDL.FRect ToFRect()
    {
        return new SDL3.SDL.FRect { X = X * Configuration.Scale, Y = Y * Configuration.Scale, W = W * Configuration.Scale, H = H * Configuration.Scale };
    }
}
