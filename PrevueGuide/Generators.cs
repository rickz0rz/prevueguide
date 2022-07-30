using System.Runtime.InteropServices;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Wrappers;
using static SDL2.SDL;
using static SDL2.SDL_ttf;

namespace PrevueGuide;

public static class Generators
{
    private static readonly SDL_Color CommonBlack = new() { a = 255, r = 17, g = 17, b = 17 };

    public enum ArrowType
    {
        None = 0,
        Single = 34,
        Double = 44
    }

    public static IntPtr GenerateDropShadowText(IntPtr renderer, IntPtr font, string text,
        SDL_Color fontColor, int scale = 1)
    {
        // var text = "\uf01d"; PREVUE
        // check font in fontforge for full characters

        const int horizontalOffset = 1;
        const int verticalOffset = 1;

        TTF_SetFontOutline(font, scale);
        using var outlineSurface = new Surface(TTF_RenderUNICODE_Blended(font, text, CommonBlack));
        using var outlineTexture = new Texture(SDL_CreateTextureFromSurface(renderer, outlineSurface.SdlSurface));

        TTF_SetFontOutline(font, 0);
        using var shadowSurface = new Surface(TTF_RenderUNICODE_Blended(font, text, CommonBlack));
        using var shadowTexture = new Texture(SDL_CreateTextureFromSurface(renderer, shadowSurface.SdlSurface));
        using var mainSurface = new Surface(TTF_RenderUNICODE_Blended(font, text, fontColor));
        using var mainTexture = new Texture(SDL_CreateTextureFromSurface(renderer, mainSurface.SdlSurface));

        var outlineSdlSurface = Marshal.PtrToStructure<SDL_Surface>(outlineSurface.SdlSurface);

        // Generate a texture that's 1 * scale wider than the outline, as its going to
        // first be used to draw the drop shadow at a 1 x 1 offset.
        var resultTexture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGBA8888,
            (int)SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, (outlineSdlSurface.w + 1 * scale),
            outlineSdlSurface.h + 1 * scale);
        _ = SDL_SetTextureBlendMode(resultTexture, SDL_BlendMode.SDL_BLENDMODE_BLEND);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(renderer, resultTexture))
        {
            _ = SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);
            _ = SDL_RenderClear(renderer);

            // Draw clock, black shadow outline
            _ = SDL_QueryTexture(outlineTexture.SdlTexture, out _, out _, out var w1, out var h1);
            var dstRect1 = new SDL_Rect
                { h = h1, w = w1, x = horizontalOffset * scale, y = verticalOffset * scale };
            _ = SDL_RenderCopy(renderer, outlineTexture.SdlTexture, IntPtr.Zero, ref dstRect1);

            // Draw clock, black shadow main
            _ = SDL_QueryTexture(shadowTexture.SdlTexture, out _, out _, out var w2, out var h2);
            var dstRect2 = new SDL_Rect
                { h = h2, w = w2, x = (horizontalOffset + 2) * scale, y = (verticalOffset + 2) * scale };
            _ = SDL_RenderCopy(renderer, shadowTexture.SdlTexture, IntPtr.Zero, ref dstRect2);

            // Draw clock, black outline
            _ = SDL_QueryTexture(outlineTexture.SdlTexture, out _, out _, out var w3, out var h3);
            var dstRect3 = new SDL_Rect
                { h = h3, w = w3, x = (horizontalOffset - 1) * scale, y = (verticalOffset - 1) * scale };
            _ = SDL_RenderCopy(renderer, outlineTexture.SdlTexture, IntPtr.Zero, ref dstRect3);

            // Draw clock, main without outline
            _ = SDL_QueryTexture(mainTexture.SdlTexture, out _, out _, out var w4, out var h4);
            var dstRect4 = new SDL_Rect
                { h = h4, w = w4, x = horizontalOffset * scale, y = verticalOffset * scale };
            _ = SDL_RenderCopy(renderer, mainTexture.SdlTexture, IntPtr.Zero, ref dstRect4);
        }

        return resultTexture;
    }

    public static IntPtr GenerateGradientFrame(TextureManager textureManager, IntPtr renderer, int width,
        int height, SDL_Color topBackgroundColor, SDL_Color backgroundColor, SDL_Color bottomBackgroundColor,
        int scale = 1)
    {
        var resultTexture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGBA8888,
            (int)SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, width * scale,
            height * scale);
        _ = SDL_SetTextureBlendMode(resultTexture, SDL_BlendMode.SDL_BLENDMODE_BLEND);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(renderer, resultTexture))
        {
            _ = SDL_SetRenderDrawColor(renderer, backgroundColor.r, backgroundColor.g, backgroundColor.b,
                backgroundColor.a);
            _ = SDL_RenderClear(renderer);

            // If the top color isn't null, then render small rectangles to fill up the frame.
            // If the bottom color isn't null, then render small rectangles to fill up the frame.

            var left = textureManager[Constants.FrameLeft];
            if (left != null)
            {
                _ = SDL_QueryTexture(left.SdlTexture, out _, out _, out var lw, out _);
                var lDstRect = new SDL_Rect { h = (height * scale), w = lw, x = 0, y = 0 };
                _ = SDL_RenderCopy(renderer, left.SdlTexture, IntPtr.Zero, ref lDstRect);
            }

            var right = textureManager[Constants.FrameRight];
            if (right != null)
            {
                _ = SDL_QueryTexture(right.SdlTexture, out _, out _, out var rw, out _);
                var rDstRect = new SDL_Rect { h = (height * scale), w = rw, x = ((width * scale) - rw), y = 0 };
                _ = SDL_RenderCopy(renderer, right.SdlTexture, IntPtr.Zero, ref rDstRect);
            }

            var upper = textureManager[Constants.FrameUpper];
            if (upper != null)
            {
                _ = SDL_QueryTexture(upper.SdlTexture, out _, out _, out _, out var upperHeight);
                var upperDstRect = new SDL_Rect { h = upperHeight, w = (width * scale), x = 0, y = 0 };
                _ = SDL_RenderCopy(renderer, upper.SdlTexture, IntPtr.Zero, ref upperDstRect);
            }

            var lower = textureManager[Constants.FrameLower];
            if (lower != null)
            {
                _ = SDL_QueryTexture(lower.SdlTexture, out _, out _, out _, out var lowerHeight);
                var lowerDstRect = new SDL_Rect
                    { h = lowerHeight, w = (width * scale), x = 0, y = (height * scale) - lowerHeight };
                _ = SDL_RenderCopy(renderer, lower.SdlTexture, IntPtr.Zero, ref lowerDstRect);
            }

            var upperLeft = textureManager[Constants.FrameUpperLeft];
            if (upperLeft != null)
            {
                _ = SDL_QueryTexture(upperLeft.SdlTexture, out _, out _, out var upperLeftWidth,
                    out var upperLeftHeight);
                var upperLeftDstRect = new SDL_Rect { h = upperLeftHeight, w = upperLeftWidth, x = 0, y = 0 };
                _ = SDL_RenderCopy(renderer, upperLeft.SdlTexture, IntPtr.Zero, ref upperLeftDstRect);
            }

            var upperRight = textureManager[Constants.FrameUpperRight];
            if (upperRight != null)
            {
                _ = SDL_QueryTexture(upperRight.SdlTexture, out _, out _, out var upperRightWidth,
                    out var upperRightHeight);
                var upperRightDstRect = new SDL_Rect
                    { h = upperRightHeight, w = upperRightWidth, x = width * scale - upperRightWidth, y = 0 };
                _ = SDL_RenderCopy(renderer, upperRight.SdlTexture, IntPtr.Zero, ref upperRightDstRect);
            }

            var lowerLeft = textureManager[Constants.FrameLowerLeft];
            if (lowerLeft != null)
            {
                _ = SDL_QueryTexture(lowerLeft.SdlTexture, out _, out _, out var lowerLeftWidth,
                    out var lowerLeftHeight);
                var lowerLeftDstRect = new SDL_Rect
                    { h = lowerLeftHeight, w = lowerLeftWidth, x = 0, y = height * scale - lowerLeftHeight };
                _ = SDL_RenderCopy(renderer, lowerLeft.SdlTexture, IntPtr.Zero, ref lowerLeftDstRect);
            }

            var lowerRight = textureManager[Constants.FrameLowerRight];
            if (lowerRight != null)
            {
                _ = SDL_QueryTexture(lowerRight.SdlTexture, out _, out _, out var lowerRightWidth,
                    out var lowerRightHeight);
                var lowerRightDstRect = new SDL_Rect
                {
                    h = lowerRightHeight, w = lowerRightWidth, x = width * scale - lowerRightWidth,
                    y = (height * scale) - lowerRightHeight
                };
                _ = SDL_RenderCopy(renderer, lowerRight.SdlTexture, IntPtr.Zero, ref lowerRightDstRect);
            }
        }

        return resultTexture;
    }

    public static IntPtr GenerateFrame(TextureManager textureManager, IntPtr renderer, int width,
        int height, SDL_Color backgroundColor, int scale = 1)
    {
        var resultTexture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGBA8888,
            (int)SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, width * scale,
            height * scale);
        _ = SDL_SetTextureBlendMode(resultTexture, SDL_BlendMode.SDL_BLENDMODE_BLEND);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(renderer, resultTexture))
        {
            _ = SDL_SetRenderDrawColor(renderer, backgroundColor.r, backgroundColor.g, backgroundColor.b,
                backgroundColor.a);
            _ = SDL_RenderClear(renderer);

            var left = textureManager[Constants.FrameLeft];
            if (left != null)
            {
                _ = SDL_QueryTexture(left.SdlTexture, out _, out _, out var lw, out _);
                var lDstRect = new SDL_Rect { h = (height * scale), w = lw, x = 0, y = 0 };
                _ = SDL_RenderCopy(renderer, left.SdlTexture, IntPtr.Zero, ref lDstRect);
            }

            var right = textureManager[Constants.FrameRight];
            if (right != null)
            {
                _ = SDL_QueryTexture(right.SdlTexture, out _, out _, out var rw, out _);
                var rDstRect = new SDL_Rect { h = (height * scale), w = rw, x = ((width * scale) - rw), y = 0 };
                _ = SDL_RenderCopy(renderer, right.SdlTexture, IntPtr.Zero, ref rDstRect);
            }

            var upper = textureManager[Constants.FrameUpper];
            if (upper != null)
            {
                _ = SDL_QueryTexture(upper.SdlTexture, out _, out _, out _, out var upperHeight);
                var upperDstRect = new SDL_Rect { h = upperHeight, w = (width * scale), x = 0, y = 0 };
                _ = SDL_RenderCopy(renderer, upper.SdlTexture, IntPtr.Zero, ref upperDstRect);
            }

            var lower = textureManager[Constants.FrameLower];
            if (lower != null)
            {
                _ = SDL_QueryTexture(lower.SdlTexture, out _, out _, out _, out var lowerHeight);
                var lowerDstRect = new SDL_Rect
                    { h = lowerHeight, w = (width * scale), x = 0, y = (height * scale) - lowerHeight };
                _ = SDL_RenderCopy(renderer, lower.SdlTexture, IntPtr.Zero, ref lowerDstRect);
            }

            var upperLeft = textureManager[Constants.FrameUpperLeft];
            if (upperLeft != null)
            {
                _ = SDL_QueryTexture(upperLeft.SdlTexture, out _, out _, out var upperLeftWidth,
                    out var upperLeftHeight);
                var upperLeftDstRect = new SDL_Rect { h = upperLeftHeight, w = upperLeftWidth, x = 0, y = 0 };
                _ = SDL_RenderCopy(renderer, upperLeft.SdlTexture, IntPtr.Zero, ref upperLeftDstRect);
            }

            var upperRight = textureManager[Constants.FrameUpperRight];
            if (upperRight != null)
            {
                _ = SDL_QueryTexture(upperRight.SdlTexture, out _, out _, out var upperRightWidth,
                    out var upperRightHeight);
                var upperRightDstRect = new SDL_Rect
                    { h = upperRightHeight, w = upperRightWidth, x = width * scale - upperRightWidth, y = 0 };
                _ = SDL_RenderCopy(renderer, upperRight.SdlTexture, IntPtr.Zero, ref upperRightDstRect);
            }

            var lowerLeft = textureManager[Constants.FrameLowerLeft];
            if (lowerLeft != null)
            {
                _ = SDL_QueryTexture(lowerLeft.SdlTexture, out _, out _, out var lowerLeftWidth,
                    out var lowerLeftHeight);
                var lowerLeftDstRect = new SDL_Rect
                    { h = lowerLeftHeight, w = lowerLeftWidth, x = 0, y = height * scale - lowerLeftHeight };
                _ = SDL_RenderCopy(renderer, lowerLeft.SdlTexture, IntPtr.Zero, ref lowerLeftDstRect);
            }

            var lowerRight = textureManager[Constants.FrameLowerRight];
            if (lowerRight != null)
            {
                _ = SDL_QueryTexture(lowerRight.SdlTexture, out _, out _, out var lowerRightWidth,
                    out var lowerRightHeight);
                var lowerRightDstRect = new SDL_Rect
                {
                    h = lowerRightHeight, w = lowerRightWidth, x = width * scale - lowerRightWidth,
                    y = (height * scale) - lowerRightHeight
                };
                _ = SDL_RenderCopy(renderer, lowerRight.SdlTexture, IntPtr.Zero, ref lowerRightDstRect);
            }
        }

        return resultTexture;
    }
}
