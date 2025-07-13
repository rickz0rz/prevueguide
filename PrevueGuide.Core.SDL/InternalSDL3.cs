using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrevueGuide.Core.SDL;

public static partial class InternalSDL3
{
    public static bool RenderFillRect(nint renderer, ScaledFRect scaledFRect)
    {
        return SDL3.SDL.RenderFillRect(renderer,  scaledFRect.ToFRect());
    }

    public static bool RenderGeometry(nint renderer, nint texture, IList<ScaledVertex> scaledVertices)
    {
        return RenderGeometry(renderer, texture, scaledVertices, []);
    }

    public static bool RenderGeometry(nint renderer, nint texture, IList<ScaledVertex> scaledVertices, int[]? indices)
    {
        var vertices = scaledVertices.Select(s => s.ToVertex()).ToArray();
        var indicesPtr = IntPtr.Zero;
        var indicesLength = 0;
        bool result;

        try
        {
            if (indices is { Length: > 0 })
            {
                indicesLength = indices.Length;
                indicesPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(int)) * indices.Length);
                Marshal.Copy(indices, 0, indicesPtr, indices.Length);
            }
            result = SDL3.SDL.RenderGeometry(renderer, texture, vertices, vertices.Length, indicesPtr, indicesLength);
        }
        finally
        {
            if (indicesPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(indicesPtr);
        }

        return result;
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
