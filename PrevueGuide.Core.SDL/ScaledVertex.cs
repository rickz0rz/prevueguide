namespace PrevueGuide.Core.SDL;

public struct ScaledVertex
{
    public ScaledFPoint Position;
    public SDL3.SDL.FColor Color;
    public ScaledFPoint TexCoord;

    public SDL3.SDL.FPoint ToFPoint()
    {
        return new SDL3.SDL.FPoint();
    }
}
