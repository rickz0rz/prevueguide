using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrevueGuide.Core.SDL;

public class SDL3Temp
{
    [DllImport("SDL3", EntryPoint = "SDL_RenderFillRect", ExactSpelling = true)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    static extern sbyte SDL_RenderFillRect(nint renderer, IntPtr fRect);

    public static void RenderFillRect(nint renderer, SDL3.SDL.FRect rect)
    {
        var fRect = SDL3.SDL.StructureToPointer<SDL3.SDL.FRect>(rect);
        SDL_RenderFillRect(renderer, fRect);
        Marshal.FreeHGlobal(fRect);
    }
}
