using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrevueGuide.Core.SDL;

public static partial class SDL3Temp
{
    [LibraryImport("SDL3")]
    private static partial sbyte SDL_RenderFillRect(nint renderer, nint fRect);

    [LibraryImport("SDL3")]
    private static partial sbyte SDL_RenderGeometry(nint renderer, nint texture, nint vertices, int numVertices, nint indices, int numIndices);

    public static void RenderFillRect(nint renderer, SDL3.SDL.FRect rect)
    {
        var marshaledFRect = SDL3.SDL.StructureToPointer<SDL3.SDL.FRect>(rect);

        try
        {
            SDL_RenderFillRect(renderer, marshaledFRect);
        }
        finally
        {
            Marshal.FreeHGlobal(marshaledFRect);
        }
    }

    public static SDL3.SDL.FColor ToFColor(this SDL3.SDL.Color color)
    {
        return new SDL3.SDL.FColor((float)color.R / 255, (float)color.G / 255, (float)color.B / 255, (float) color.A / 255);
    }

    public static bool SetRenderDrawColor(nint renderer, SDL3.SDL.Color color)
    {
        return SDL3.SDL.SetRenderDrawColor(renderer, color.R, color.G, color.B, color.A);
    }
}
