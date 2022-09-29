using System.Runtime.InteropServices;
using System.Security;
using DearImguiSharp;
using __CallingConvention = global::System.Runtime.InteropServices.CallingConvention;
using __IntPtr = global::System.IntPtr;

namespace PrevueGuide;

public unsafe partial class ImGuiExtras
{
    public partial struct __Internal
    {
        // ImGui_ImplSDLRenderer_Init
        [SuppressUnmanagedCodeSecurity, DllImport("cimgui", EntryPoint = "ImGui_ImplSDLRenderer_Init", CallingConvention = __CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ImGuiImplSDLRendererInit(__IntPtr window);

        // ImGui_ImplSDLRenderer_NewFrame
        [SuppressUnmanagedCodeSecurity, DllImport("cimgui", EntryPoint = "ImGui_ImplSDLRenderer_NewFrame", CallingConvention = __CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern void ImGuiImplSDLRendererNewFrame();

        [SuppressUnmanagedCodeSecurity, DllImport("cimgui", EntryPoint = "ImGui_ImplSDLRenderer_RenderDrawData", CallingConvention = __CallingConvention.Cdecl)]
        public static extern void ImGuiImplSDLRendererRenderDrawData(__IntPtr draw_data);
    }

    public static bool ImGuiImplSDLRendererInit(global::DearImguiSharp.SDL_Renderer renderer)
    {
        var __arg0 = renderer is null ? __IntPtr.Zero : renderer.__Instance;
        var __ret = __Internal.ImGuiImplSDLRendererInit(__arg0);
        return __ret;
    }

    public static void ImGuiImplSDLRendererNewFrame()
    {
        __Internal.ImGuiImplSDLRendererNewFrame();
    }

    public static void ImGuiImplSDLRendererRenderDrawData(global::DearImguiSharp.ImDrawData draw_data)
    {
        var __arg0 = draw_data is null ? __IntPtr.Zero : draw_data.__Instance;
        __Internal.ImGuiImplSDLRendererRenderDrawData(__arg0);
    }

    public static (SDL_Window, SDL_Renderer) ConvertObjects(IntPtr window, IntPtr renderer)
    {
        return (new SDL_Window(window.ToPointer()), new SDL_Renderer(renderer.ToPointer()));
    }

    public static SDL_Event ConvertEvent(IntPtr sdlEvent)
    {
        return new SDL_Event(sdlEvent.ToPointer());
    }
}
