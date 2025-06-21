using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide;

public static class Generators
{
    private static readonly SDL3.SDL.Color CommonBlack = new() { A = 255, R = 17, G = 17, B = 17 };

    public enum ArrowType
    {
        None = 0,
        Single = 34,
        Double = 44
    }

    public static IntPtr GenerateDropShadowText(IntPtr renderer, IntPtr font, string text,
        SDL3.SDL.Color fontColor, int scale = 1)
    {
        // var text = "\uf01d"; PREVUE
        // check font in fontforge for full characters

        const int horizontalOffset = 1;
        const int verticalOffset = 1;

        SDL3.TTF.SetFontOutline(font, scale);
        using var outlineSurface = new Surface(SDL3.TTF.RenderTextBlended(font, text, 0, CommonBlack));
        using var outlineTexture = new Texture(SDL3.SDL.CreateTextureFromSurface(renderer, outlineSurface.SdlSurface));

        SDL3.TTF.SetFontOutline(font, 0);
        using var shadowSurface = new Surface(SDL3.TTF.RenderTextBlended(font, text, 0, CommonBlack));
        using var shadowTexture = new Texture(SDL3.SDL.CreateTextureFromSurface(renderer, shadowSurface.SdlSurface));
        using var mainSurface = new Surface(SDL3.TTF.RenderTextBlended(font, text, 0, fontColor));
        using var mainTexture = new Texture(SDL3.SDL.CreateTextureFromSurface(renderer, mainSurface.SdlSurface));

        var outlineSdlSurface = Marshal.PtrToStructure<SDL3.SDL.Surface>(outlineSurface.SdlSurface);

        // Generate a texture that's 1 * scale wider than the outline, as its going to
        // first be used to draw the drop shadow at a 1 x 1 offset.
        var resultTexture = SDL3.SDL.CreateTexture(renderer, SDL3.SDL.PixelFormat.RGBA8888,
            SDL3.SDL.TextureAccess.Target, (outlineSdlSurface.Width + 1 * scale),
            outlineSdlSurface.Height + 1 * scale);
        _ = SDL3.SDL.SetTextureBlendMode(resultTexture, SDL3.SDL.BlendMode.Blend);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(renderer, resultTexture))
        {
            _ = SDL3.SDL.SetRenderDrawColor(renderer, 0, 0, 0, 0);
            _ = SDL3.SDL.RenderClear(renderer);

            // Draw clock, black shadow outline
            _ = SDL3.SDL.GetTextureSize(outlineTexture.SdlTexture, out var w1, out var h1);
            var dstRect1 = new SDL3.SDL.FRect
                { H = h1, W = w1, X = horizontalOffset * scale, Y = verticalOffset * scale };
            _ = SDL3.SDL.RenderTexture(renderer, outlineTexture.SdlTexture, IntPtr.Zero, in dstRect1);

            // Draw clock, black shadow main
            _ = SDL3.SDL.GetTextureSize(shadowTexture.SdlTexture, out var w2, out var h2);
            var dstRect2 = new SDL3.SDL.FRect
                { H = h2, W = w2, X = (horizontalOffset + 2) * scale, Y = (verticalOffset + 2) * scale };
            _ = SDL3.SDL.RenderTexture(renderer, shadowTexture.SdlTexture, IntPtr.Zero, in dstRect2);

            // Draw clock, black outline
            _ = SDL3.SDL.GetTextureSize(outlineTexture.SdlTexture, out var w3, out var h3);
            var dstRect3 = new SDL3.SDL.FRect
                { H = h3, W = w3, X = (horizontalOffset - 1) * scale, Y = (verticalOffset - 1) * scale };
            _ = SDL3.SDL.RenderTexture(renderer, outlineTexture.SdlTexture, IntPtr.Zero, in dstRect3);

            // Draw clock, main without outline
            _ = SDL3.SDL.GetTextureSize(mainTexture.SdlTexture, out var w4, out var h4);
            var dstRect4 = new SDL3.SDL.FRect
                { H = h4, W = w4, X = horizontalOffset * scale, Y = verticalOffset * scale };
            _ = SDL3.SDL.RenderTexture(renderer, mainTexture.SdlTexture, IntPtr.Zero, in dstRect4);
        }

        return resultTexture;
    }

    public static IntPtr GenerateGradientFrame(TextureManager textureManager, IntPtr renderer, int width,
        int height, SDL3.SDL.Color topBackgroundColor, SDL3.SDL.Color backgroundColor, SDL3.SDL.Color bottomBackgroundColor,
        int scale = 1)
    {
        var resultTexture = SDL3.SDL.CreateTexture(renderer, SDL3.SDL.PixelFormat.RGBA8888,
            SDL3.SDL.TextureAccess.Target, width * scale, height * scale);
        _ = SDL3.SDL.SetTextureBlendMode(resultTexture, SDL3.SDL.BlendMode.Blend);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(renderer, resultTexture))
        {
            _ = SDL3.SDL.SetRenderDrawColor(renderer, backgroundColor.R, backgroundColor.G, backgroundColor.B,
                backgroundColor.A);
            _ = SDL3.SDL.RenderClear(renderer);

            // If the top color isn't null, then render small rectangles to fill up the frame.
            // If the bottom color isn't null, then render small rectangles to fill up the frame.

            var left = textureManager[Constants.FrameLeft];
            if (left != null)
            {
                _ = SDL3.SDL.GetTextureSize(left.SdlTexture, out var lw, out _);
                var lDstRect = new SDL3.SDL.FRect { H = (height * scale), W = lw, X = 0, Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, left.SdlTexture, IntPtr.Zero, in lDstRect);
            }

            var right = textureManager[Constants.FrameRight];
            if (right != null)
            {
                _ = SDL3.SDL.GetTextureSize(right.SdlTexture, out var rw, out _);
                var rDstRect = new SDL3.SDL.FRect { H = (height * scale), W = rw, X = ((width * scale) - rw), Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, right.SdlTexture, IntPtr.Zero, rDstRect);
            }

            var upper = textureManager[Constants.FrameUpper];
            if (upper != null)
            {
                _ = SDL3.SDL.GetTextureSize(upper.SdlTexture, out _, out var upperHeight);
                var upperDstRect = new SDL3.SDL.FRect { H = upperHeight, W = (width * scale), X = 0, Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, upper.SdlTexture, IntPtr.Zero, in upperDstRect);
            }

            var lower = textureManager[Constants.FrameLower];
            if (lower != null)
            {
                _ = SDL3.SDL.GetTextureSize(lower.SdlTexture, out _, out var lowerHeight);
                var lowerDstRect = new SDL3.SDL.FRect
                    { H = lowerHeight, W = (width * scale), X = 0, Y = (height * scale) - lowerHeight };
                _ = SDL3.SDL.RenderTexture(renderer, lower.SdlTexture, IntPtr.Zero, in lowerDstRect);
            }

            var upperLeft = textureManager[Constants.FrameUpperLeft];
            if (upperLeft != null)
            {
                _ = SDL3.SDL.GetTextureSize(upperLeft.SdlTexture, out var upperLeftWidth,
                    out var upperLeftHeight);
                var upperLeftDstRect = new SDL3.SDL.FRect { H = upperLeftHeight, W = upperLeftWidth, X = 0, Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, upperLeft.SdlTexture, IntPtr.Zero, in upperLeftDstRect);
            }

            var upperRight = textureManager[Constants.FrameUpperRight];
            if (upperRight != null)
            {
                _ = SDL3.SDL.GetTextureSize(upperRight.SdlTexture, out var upperRightWidth,
                    out var upperRightHeight);
                var upperRightDstRect = new SDL3.SDL.FRect
                    { H = upperRightHeight, W = upperRightWidth, X = width * scale - upperRightWidth, Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, upperRight.SdlTexture, IntPtr.Zero, in upperRightDstRect);
            }

            var lowerLeft = textureManager[Constants.FrameLowerLeft];
            if (lowerLeft != null)
            {
                _ = SDL3.SDL.GetTextureSize(lowerLeft.SdlTexture, out var lowerLeftWidth,
                    out var lowerLeftHeight);
                var lowerLeftDstRect = new SDL3.SDL.FRect
                    { H = lowerLeftHeight, W = lowerLeftWidth, X = 0, Y = height * scale - lowerLeftHeight };
                _ = SDL3.SDL.RenderTexture(renderer, lowerLeft.SdlTexture, IntPtr.Zero, in lowerLeftDstRect);
            }

            var lowerRight = textureManager[Constants.FrameLowerRight];
            if (lowerRight != null)
            {
                _ = SDL3.SDL.GetTextureSize(lowerRight.SdlTexture, out var lowerRightWidth,
                    out var lowerRightHeight);
                var lowerRightDstRect = new SDL3.SDL.FRect
                {
                    H = lowerRightHeight, W = lowerRightWidth, X = width * scale - lowerRightWidth,
                    Y = (height * scale) - lowerRightHeight
                };
                _ = SDL3.SDL.RenderTexture(renderer, lowerRight.SdlTexture, IntPtr.Zero, in lowerRightDstRect);
            }
        }

        return resultTexture;
    }

    public static IntPtr GenerateFrame(TextureManager textureManager, IntPtr renderer, int width,
        int height, SDL3.SDL.Color backgroundColor, int scale = 1)
    {
        var resultTexture = SDL3.SDL.CreateTexture(renderer, SDL3.SDL.PixelFormat.RGBA8888,
            SDL3.SDL.TextureAccess.Target, width * scale, height * scale);
        _ = SDL3.SDL.SetTextureBlendMode(resultTexture, SDL3.SDL.BlendMode.Blend);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(renderer, resultTexture))
        {
            _ = SDL3.SDL.SetRenderDrawColor(renderer, backgroundColor.R, backgroundColor.G, backgroundColor.B,
                backgroundColor.A);
            _ = SDL3.SDL.RenderClear(renderer);

            var left = textureManager[Constants.FrameLeft];
            if (left != null)
            {
                _ = SDL3.SDL.GetTextureSize(left.SdlTexture, out var lw, out _);
                var lDstRect = new SDL3.SDL.FRect { H = (height * scale), W = lw, X = 0, Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, left.SdlTexture, IntPtr.Zero, in lDstRect);
            }

            var right = textureManager[Constants.FrameRight];
            if (right != null)
            {
                _ = SDL3.SDL.GetTextureSize(right.SdlTexture, out var rw, out _);
                var rDstRect = new SDL3.SDL.FRect { H = (height * scale), W = rw, X = ((width * scale) - rw), Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, right.SdlTexture, IntPtr.Zero, in rDstRect);
            }

            var upper = textureManager[Constants.FrameUpper];
            if (upper != null)
            {
                _ = SDL3.SDL.GetTextureSize(upper.SdlTexture, out _, out var upperHeight);
                var upperDstRect = new SDL3.SDL.FRect { H = upperHeight, W = (width * scale), X = 0, Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, upper.SdlTexture, IntPtr.Zero, in upperDstRect);
            }

            var lower = textureManager[Constants.FrameLower];
            if (lower != null)
            {
                _ = SDL3.SDL.GetTextureSize(lower.SdlTexture, out _, out var lowerHeight);
                var lowerDstRect = new SDL3.SDL.FRect
                    { H = lowerHeight, W = (width * scale), X = 0, Y = (height * scale) - lowerHeight };
                _ = SDL3.SDL.RenderTexture(renderer, lower.SdlTexture, IntPtr.Zero, in lowerDstRect);
            }

            var upperLeft = textureManager[Constants.FrameUpperLeft];
            if (upperLeft != null)
            {
                _ = SDL3.SDL.GetTextureSize(upperLeft.SdlTexture, out var upperLeftWidth,
                    out var upperLeftHeight);
                var upperLeftDstRect = new SDL3.SDL.FRect { H = upperLeftHeight, W = upperLeftWidth, X = 0, Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, upperLeft.SdlTexture, IntPtr.Zero, in upperLeftDstRect);
            }

            var upperRight = textureManager[Constants.FrameUpperRight];
            if (upperRight != null)
            {
                _ = SDL3.SDL.GetTextureSize(upperRight.SdlTexture, out var upperRightWidth,
                    out var upperRightHeight);
                var upperRightDstRect = new SDL3.SDL.FRect
                    { H = upperRightHeight, W = upperRightWidth, X = width * scale - upperRightWidth, Y = 0 };
                _ = SDL3.SDL.RenderTexture(renderer, upperRight.SdlTexture, IntPtr.Zero, in upperRightDstRect);
            }

            var lowerLeft = textureManager[Constants.FrameLowerLeft];
            if (lowerLeft != null)
            {
                _ = SDL3.SDL.GetTextureSize(lowerLeft.SdlTexture, out var lowerLeftWidth,
                    out var lowerLeftHeight);
                var lowerLeftDstRect = new SDL3.SDL.FRect
                    { H = lowerLeftHeight, W = lowerLeftWidth, X = 0, Y = height * scale - lowerLeftHeight };
                _ = SDL3.SDL.RenderTexture(renderer, lowerLeft.SdlTexture, IntPtr.Zero, in lowerLeftDstRect);
            }

            var lowerRight = textureManager[Constants.FrameLowerRight];
            if (lowerRight != null)
            {
                _ = SDL3.SDL.GetTextureSize(lowerRight.SdlTexture, out var lowerRightWidth,
                    out var lowerRightHeight);
                var lowerRightDstRect = new SDL3.SDL.FRect
                {
                    H = lowerRightHeight, W = lowerRightWidth, X = width * scale - lowerRightWidth,
                    Y = (height * scale) - lowerRightHeight
                };
                _ = SDL3.SDL.RenderTexture(renderer, lowerRight.SdlTexture, IntPtr.Zero, lowerRightDstRect);
            }
        }

        return resultTexture;
    }
}
