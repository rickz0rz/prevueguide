using System.Runtime.InteropServices;
using SDL2;

namespace PrevueGuide;

public static class Generators
{
    public static IntPtr LoadImageToTexture(IntPtr renderer, string filename)
    {
        // If I decide to pack everything into a zip or something...
        // var src = SDL_RWFromMem(...)
        // SDL_image.IMG_Load_RW(src, 1)

        var surface = SDL_image.IMG_Load(filename);
        var texture = SDL.SDL_CreateTextureFromSurface(renderer, surface);
        _ = SDL.SDL_SetTextureBlendMode(texture, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
        SDL.SDL_FreeSurface(surface);
        return texture;
    }

    public static IntPtr GenerateDropShadowText(IntPtr renderer, IntPtr font, string text,
        SDL.SDL_Color fontColor, int scale = 1)
    {
        const int horizontalOffset = 1;
        const int verticalOffset = 1;

        var black = new SDL.SDL_Color { a = 255, r = 17, g = 17, b = 17 };

        SDL_ttf.TTF_SetFontOutline(font, scale);
        var outlineSurface = SDL_ttf.TTF_RenderText_Blended(font, text, black);
        var outlineTexture = SDL.SDL_CreateTextureFromSurface(renderer, outlineSurface);

        SDL_ttf.TTF_SetFontOutline(font, 0);
        var shadowSurface = SDL_ttf.TTF_RenderText_Blended(font, text, black);
        var shadowTexture = SDL.SDL_CreateTextureFromSurface(renderer, shadowSurface);
        var mainSurface = SDL_ttf.TTF_RenderText_Blended(font, text, fontColor);
        var mainTexture = SDL.SDL_CreateTextureFromSurface(renderer, mainSurface);

        var outlineSdlSurface = Marshal.PtrToStructure<SDL.SDL_Surface>(outlineSurface);

        // Generate a texture that's 1 * scale wider than the outline, as its going to
        // first be used to draw the drop shadow at a 1 x 1 offset.
        var resultTexture = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGBA8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, (outlineSdlSurface.w + 1 * scale),
            outlineSdlSurface.h + 1 * scale);
        _ = SDL.SDL_SetTextureBlendMode(resultTexture, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);

        // Switch to the texture for rendering.
        _ = SDL.SDL_SetRenderTarget(renderer, resultTexture);

        // Draw clock, black shadow outline
        _ = SDL.SDL_QueryTexture(outlineTexture, out _, out _, out var w1, out var h1);
        var dstRect1 = new SDL.SDL_Rect { h = h1, w = w1, x = horizontalOffset * scale, y = verticalOffset * scale };
        SDL.SDL_RenderCopy(renderer, outlineTexture, IntPtr.Zero, ref dstRect1);

        // Draw clock, black shadow main
        _ = SDL.SDL_QueryTexture(shadowTexture, out _, out _, out var w2, out var h2);
        var dstRect2 = new SDL.SDL_Rect
            { h = h2, w = w2, x = (horizontalOffset + 2) * scale, y = (verticalOffset + 2) * scale };
        SDL.SDL_RenderCopy(renderer, shadowTexture, IntPtr.Zero, ref dstRect2);

        // Draw clock, black outline
        _ = SDL.SDL_QueryTexture(outlineTexture, out _, out _, out var w3, out var h3);
        var dstRect3 = new SDL.SDL_Rect
            { h = h3, w = w3, x = (horizontalOffset - 1) * scale, y = (verticalOffset - 1) * scale };
        SDL.SDL_RenderCopy(renderer, outlineTexture, IntPtr.Zero, ref dstRect3);

        // Draw clock, main without outline
        _ = SDL.SDL_QueryTexture(mainTexture, out _, out _, out var w4, out var h4);
        var dstRect4 = new SDL.SDL_Rect { h = h4, w = w4, x = horizontalOffset * scale, y = verticalOffset * scale };
        SDL.SDL_RenderCopy(renderer, mainTexture, IntPtr.Zero, ref dstRect4);

        // Switch back to the main render target.
        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);

        // Clean up
        SDL.SDL_DestroyTexture(outlineTexture);
        SDL.SDL_DestroyTexture(shadowTexture);
        SDL.SDL_DestroyTexture(mainTexture);
        SDL.SDL_FreeSurface(mainSurface);
        SDL.SDL_FreeSurface(shadowSurface);
        SDL.SDL_FreeSurface(outlineSurface);

        return resultTexture;
    }

    public static IntPtr GenerateFrame(IntPtr renderer, int width, int height,
        SDL.SDL_Color backgroundColor, int scale = 1)
    {
        var upperLeft = LoadImageToTexture(renderer, $"assets/frame_upper_left_2x_smooth.png");
        var upperRight = LoadImageToTexture(renderer, $"assets/frame_upper_right_2x_smooth.png");
        var lowerLeft = LoadImageToTexture(renderer, $"assets/frame_lower_left_2x_smooth.png");
        var lowerRight = LoadImageToTexture(renderer, $"assets/frame_lower_right_2x_smooth.png");
        var left = LoadImageToTexture(renderer, $"assets/frame_left_2x.png");
        var right = LoadImageToTexture(renderer, $"assets/frame_right_2x.png");
        var upper = LoadImageToTexture(renderer, $"assets/frame_upper_2x.png");
        var lower = LoadImageToTexture(renderer, $"assets/frame_lower_2x.png");

        var resultTexture = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGBA8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, width * scale,
            height * scale);
        _ = SDL.SDL_SetTextureBlendMode(resultTexture, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);

        // Switch to the texture for rendering.
        _ = SDL.SDL_SetRenderTarget(renderer, resultTexture);

        _ = SDL.SDL_SetRenderDrawColor(renderer, backgroundColor.r, backgroundColor.g, backgroundColor.b,
            backgroundColor.a);
        _ = SDL.SDL_RenderClear(renderer);

        _ = SDL.SDL_QueryTexture(left, out uint _, out int _, out int lw, out int lh);
        var lDstRect = new SDL.SDL_Rect { h = (height * scale), w = lw, x = 0, y = 0 };
        _ = SDL.SDL_RenderCopy(renderer, left, IntPtr.Zero, ref lDstRect);

        _ = SDL.SDL_QueryTexture(right, out uint _, out int _, out int rw, out int rh);
        var rDstRect = new SDL.SDL_Rect { h = (height * scale), w = rw, x = ((width * scale) - rw), y = 0 };
        _ = SDL.SDL_RenderCopy(renderer, right, IntPtr.Zero, ref rDstRect);

        _ = SDL.SDL_QueryTexture(upper, out uint _, out int _, out int uw, out int uh);
        var uDstRect = new SDL.SDL_Rect { h = uh, w = (width * scale), x = 0, y = 0 };
        _ = SDL.SDL_RenderCopy(renderer, upper, IntPtr.Zero, ref uDstRect);

        _ = SDL.SDL_QueryTexture(lower, out uint _, out int _, out int lowerWidth, out int lowerHeight);
        var lowerDstRect = new SDL.SDL_Rect
            { h = lowerHeight, w = (width * scale), x = 0, y = (height * scale) - lowerHeight };
        _ = SDL.SDL_RenderCopy(renderer, lower, IntPtr.Zero, ref lowerDstRect);

        _ = SDL.SDL_QueryTexture(upperLeft, out uint _, out int _, out int ulw, out int ulh);
        var ulDstRect = new SDL.SDL_Rect { h = ulh, w = ulw, x = 0, y = 0 };
        _ = SDL.SDL_RenderCopy(renderer, upperLeft, IntPtr.Zero, ref ulDstRect);

        _ = SDL.SDL_QueryTexture(upperRight, out uint _, out int _, out int urw, out int urh);
        var urDstRect = new SDL.SDL_Rect { h = urh, w = urw, x = (width * scale) - urw, y = 0 };
        _ = SDL.SDL_RenderCopy(renderer, upperRight, IntPtr.Zero, ref urDstRect);

        _ = SDL.SDL_QueryTexture(lowerLeft, out _, out _, out var lowerLeftWidth, out var lowerLeftHeight);
        var lowerLeftDstRect = new SDL.SDL_Rect
            { h = lowerLeftHeight, w = lowerLeftWidth, x = 0, y = (height * scale) - lowerLeftHeight };
        _ = SDL.SDL_RenderCopy(renderer, lowerLeft, IntPtr.Zero, ref lowerLeftDstRect);

        _ = SDL.SDL_QueryTexture(lowerRight, out _, out _, out var lrw, out var lrh);
        var lowerRightDstRect = new SDL.SDL_Rect
            { h = urh, w = urw, x = (width * scale) - lrw, y = (height * scale) - lrh };
        _ = SDL.SDL_RenderCopy(renderer, lowerRight, IntPtr.Zero, ref lowerRightDstRect);

        // Switch back to the main render target.
        _ = SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);

        SDL.SDL_DestroyTexture(upperLeft);
        SDL.SDL_DestroyTexture(upperRight);
        SDL.SDL_DestroyTexture(lowerLeft);
        SDL.SDL_DestroyTexture(lowerRight);
        SDL.SDL_DestroyTexture(left);
        SDL.SDL_DestroyTexture(right);
        SDL.SDL_DestroyTexture(upper);
        SDL.SDL_DestroyTexture(lower);

        return resultTexture;
    }
}
