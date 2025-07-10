using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrevueGuide.Core.SDL;

public static partial class InternalSDL3
{
    [LibraryImport("SDL3")]
    private static partial sbyte SDL_RenderFillRect(nint renderer, nint fRect);

    [LibraryImport("SDL3")]
    private static partial sbyte SDL_RenderGeometry(nint renderer, nint texture, nint vertices, int numVertices, nint indices, int numIndices);

    public static bool RenderFillRect(nint renderer, ScaledFRect scaledFRect)
    {
        var marshaledFRect = SDL3.SDL.StructureToPointer<SDL3.SDL.FRect>(scaledFRect.ToFRect());

        try
        {
            return SDL_RenderFillRect(renderer, marshaledFRect) == 1;
        }
        finally
        {
            Marshal.FreeHGlobal(marshaledFRect);
        }
    }

    public static bool RenderFillRect(nint renderer, SDL3.SDL.FRect rect)
    {
        var marshaledFRect = SDL3.SDL.StructureToPointer<SDL3.SDL.FRect>(rect);

        try
        {
            return SDL_RenderFillRect(renderer, marshaledFRect) == 1;
        }
        finally
        {
            Marshal.FreeHGlobal(marshaledFRect);
        }
    }

    public static bool RenderGeometry(nint renderer, nint texture, IList<ScaledVertex> vertices, IList<int>? indices)
    {
        var verticesPtr = IntPtr.Zero;
        var indicesPtr = IntPtr.Zero;

        try
        {
            var regularVertexes = vertices.Select(vertex => vertex.ToVertex());

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

    [LibraryImport("SDL3_ttf", EntryPoint = "TTF_GetTextSize"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    // [return: MarshalAs(UnmanagedType.I1)]
    private static partial sbyte GetTextSize(nint text, nint w, nint h);

    public static bool GetTextSize(nint text, out int w, out int h)
    {
        var wp = IntPtr.Zero;
        var hp = IntPtr.Zero;

        var retval = GetTextSize(text, wp, hp);

        w = (int)wp;
        h = (int)hp;

        Marshal.FreeHGlobal(wp);
        Marshal.FreeHGlobal(hp);

        return retval == 1;
    }
}
