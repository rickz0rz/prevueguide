using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL.Esquire;

public static class Frame
{
    public static void CreateBevelOnTexture(nint renderer, Texture texture, int scale, int bevelSize = 4)
    {
        _ = SDL3.SDL.GetTextureSize(texture.SdlTexture, out var width, out var height);

        // When translating to a Rect, remove the scale.
        var rect = new SDL3.SDL.Rect
        {
            W = (int)width / scale,
            H = (int)height / scale,
            X = 0,
            Y = 0
        };

        CreateBevelOnTexture(renderer, texture, scale, rect, bevelSize);
    }

    public static void CreateBevelOnTexture(nint renderer, Texture texture, int scale, SDL3.SDL.Rect rect, int bevelSize = 4)
    {
        var frameBevelHighlight = Colors.Gray170;
        var frameBevelShadow = Colors.Black17;
        var frameBevelShadowCorner = Colors.Gray85;

        var oldRenderTarget = SDL3.SDL.GetRenderTarget(renderer);
        _ = SDL3.SDL.SetRenderTarget(renderer, texture.SdlTexture);

        // Highlight: Top and left edges.
        _ = SDL3Temp.SetRenderDrawColor(renderer, frameBevelHighlight);
        foreach (var scaledFRect in new[]
                 {
                     new ScaledFRect { H = bevelSize, W = rect.W - bevelSize, X = rect.X, Y = rect.Y, Scale = scale },
                     new ScaledFRect { H = rect.H - (bevelSize * 2), W = bevelSize, X = rect.X, Y = rect.Y + bevelSize, Scale = scale }
                 })
        {
            SDL3Temp.RenderFillRect(renderer, scaledFRect.ToFRect());
        }

        // Shadow: bottom and right edges.
        _ = SDL3Temp.SetRenderDrawColor(renderer, frameBevelShadow);
        foreach (var scaledFRect in new[]
                 {
                     new ScaledFRect { H = bevelSize, W = rect.W, X = rect.X, Y = rect.Y + rect.H - bevelSize, Scale = scale },
                     new ScaledFRect { H = rect.H, W = bevelSize, X = rect.X + rect.W - bevelSize, Y = rect.Y, Scale = scale }
                 })
        {
            SDL3Temp.RenderFillRect(renderer, scaledFRect.ToFRect());
        }

        // Draw white triangle, bottom left.
        _ = SDL3Temp.SetRenderDrawColor(renderer, frameBevelHighlight);
        var vertexListBottomLeft = new List<SDL3.SDL.Vertex>
        {
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.X, Y = rect.H + rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.X, Y = rect.H - bevelSize + rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = bevelSize + rect.X, Y = rect.H - bevelSize + rect.Y, Scale = scale }.ToFPoint()
            }
        };
        _ = SDL3.SDL.RenderGeometry(renderer, IntPtr.Zero, vertexListBottomLeft.ToArray(), vertexListBottomLeft.Count, IntPtr.Zero, 0);

        // Draw white triangle, upper right.
        var vertexListUpperRight = new List<SDL3.SDL.Vertex>
        {
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - bevelSize + rect.X, Y = rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - bevelSize + rect.X, Y = bevelSize + rect.Y, Scale = scale }.ToFPoint()
            }
        };
        _ = SDL3.SDL.RenderGeometry(renderer, IntPtr.Zero, vertexListUpperRight.ToArray(), vertexListUpperRight.Count, IntPtr.Zero, 0);

        // Render a block on the top-left that's going to be black to render triangles on
        _ = SDL3Temp.SetRenderDrawColor(renderer, frameBevelShadow);
        foreach (var scaledFRect in new[]
                 {
                     new ScaledFRect { H = bevelSize + 1, W = bevelSize, X = rect.X, Y = rect.Y, Scale = scale },
                     new ScaledFRect { H = bevelSize, W = bevelSize + 1, X = rect.X, Y = rect.Y, Scale = scale }
                 })
        {
            SDL3Temp.RenderFillRect(renderer, scaledFRect.ToFRect());
        }

        // Draw white triangles, upper left
        var vertexListUpperLeftA = new List<SDL3.SDL.Vertex>
        {
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.X, Y = 1 + rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.X, Y = bevelSize + 1 + rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = bevelSize + rect.X, Y = bevelSize + 1 + rect.Y, Scale = scale }.ToFPoint()
            }
        };
        _ = SDL3.SDL.RenderGeometry(renderer, IntPtr.Zero, vertexListUpperLeftA.ToArray(), vertexListUpperLeftA.Count, IntPtr.Zero, 0);

        var vertexListUpperLeftB = new List<SDL3.SDL.Vertex>
        {
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = 1 + rect.X, Y = rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = bevelSize + 1 + rect.X, Y = rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = bevelSize + 1 + rect.X, Y = bevelSize + rect.Y, Scale = scale }.ToFPoint()
            }
        };
        _ = SDL3.SDL.RenderGeometry(renderer, IntPtr.Zero, vertexListUpperLeftB.ToArray(), vertexListUpperLeftB.Count, IntPtr.Zero, 0);

        // Render a block on the bottom-right that's going to be gray to render triangles on
        _ = SDL3Temp.SetRenderDrawColor(renderer, frameBevelShadowCorner);
        foreach (var scaledFRect in new[]
                 {
                     new ScaledFRect { H = bevelSize + 1, W = bevelSize, X = rect.W - bevelSize + rect.X, Y = rect.H - (bevelSize + 1) + rect.Y, Scale = scale },
                     new ScaledFRect { H = bevelSize, W = bevelSize + 1, X = rect.W - (bevelSize + 1) + rect.X, Y = rect.H - bevelSize + rect.Y, Scale = scale }
                 })
        {
            SDL3Temp.RenderFillRect(renderer, scaledFRect.ToFRect());
        }

        // Draw black triangles, lower right
        _ = SDL3Temp.SetRenderDrawColor(renderer, frameBevelShadow);
        var vertexListLowerRightA = new List<SDL3.SDL.Vertex>
        {
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.H - 1 + rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.H - (bevelSize + 1) + rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - bevelSize + rect.X, Y = rect.H - (bevelSize + 1) + rect.Y, Scale = scale }.ToFPoint()
            }
        };
        _ = SDL3.SDL.RenderGeometry(renderer, IntPtr.Zero, vertexListLowerRightA.ToArray(), vertexListLowerRightA.Count, IntPtr.Zero, 0);

        var vertexListLowerRightB = new List<SDL3.SDL.Vertex>
        {
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - 1 + rect.X, Y = rect.H + rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - (bevelSize + 1) + rect.X, Y = rect.H + rect.Y, Scale = scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - (bevelSize + 1) + rect.X, Y = rect.H - bevelSize + rect.Y, Scale = scale }.ToFPoint()
            }
        };
        _ = SDL3.SDL.RenderGeometry(renderer, IntPtr.Zero, vertexListLowerRightB.ToArray(), vertexListLowerRightB.Count, IntPtr.Zero, 0);

        _ = SDL3.SDL.SetRenderTarget(renderer, oldRenderTarget);
    }
}
