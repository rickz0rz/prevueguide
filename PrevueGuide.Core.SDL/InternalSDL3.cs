using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrevueGuide.Core.SDL;

public static partial class InternalSDL3
{

    [LibraryImport("SDL3")]
    private static partial sbyte SDL_RenderGeometry(nint renderer, nint texture, nint vertices, int numVertices, nint indices, int numIndices);

    public static bool RenderFillRect(nint renderer, ScaledFRect scaledFRect)
    {
        return SDL3.SDL.RenderFillRect(renderer,  scaledFRect.ToFRect());
    }

    public static bool RenderGeometry(nint renderer, nint texture, IList<ScaledVertex> scaledVertices, int[] indices)
    {
        /*
        var verticesPtr = IntPtr.Zero;
        var indicesPtr = IntPtr.Zero;

        try
        {
            var regularVertexes = scaledVertices.Select(vertex => vertex.ToVertex());

            verticesPtr = SDL3.SDL.StructureArrayToPointer(regularVertexes.ToArray());
            indicesPtr = indices == null || indices.Count == 0
                ? IntPtr.Zero
                : SDL3.SDL.StructureArrayToPointer(indices.ToArray());

            var numIndices = indices?.Count ?? 0;
            return SDL_RenderGeometry(renderer, texture, verticesPtr, vertices.Count, indicesPtr, numIndices) == 1;
        }
        finally
        {
            if (verticesPtr != IntPtr.Zero) Marshal.FreeHGlobal(verticesPtr);
            if (indicesPtr != IntPtr.Zero) Marshal.FreeHGlobal(indicesPtr);
        }*/

        var vertices = scaledVertices.Select(s => s.ToVertex()).ToArray();
        // Something acts funny here with indices.. Might need to marshal the array. (See above.)
        return SDL3.SDL.RenderGeometry(renderer, texture, vertices, vertices.Length, IntPtr.Zero, 0);
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
