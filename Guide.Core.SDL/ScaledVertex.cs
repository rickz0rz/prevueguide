namespace Guide.Core.SDL;

public struct ScaledVertex
{
    public ScaledFPoint Position;
    public SDL3.SDL.FColor Color;
    public ScaledFPoint TexCoord;

    public SDL3.SDL.Vertex ToVertex()
    {
        return new SDL3.SDL.Vertex
        {
            Color = Color,
            Position = new SDL3.SDL.FPoint
                { X = Position.X * Configuration.Scale, Y = Position.Y * Configuration.Scale },
            TexCoord = new SDL3.SDL.FPoint
                { X = TexCoord.X * Configuration.Scale, Y = TexCoord.Y * Configuration.Scale },
        };
    }
}
