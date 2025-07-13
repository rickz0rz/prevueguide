using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL.Esquire;

public static class Frame
{
    public const int BevelSize = 4;

    public static void CreateBevelOnTexture(nint renderer, Texture texture, int bevelSize = BevelSize)
    {
        _ = SDL3.SDL.GetTextureSize(texture.SdlTexture, out var width, out var height);

        var rect = new SDL3.SDL.Rect
        {
            W = (int)width / Configuration.Scale,
            H = (int)height / Configuration.Scale,
            X = 0,
            Y = 0
        };

        CreateBevelOnTexture(renderer, texture, rect, bevelSize);
    }

    public static void CreateBevelOnTexture(nint renderer, Texture texture, SDL3.SDL.Rect rect, int bevelSize = 4)
    {
        var frameBevelHighlight = Colors.Gray170;
        var frameBevelShadow = Colors.Black17;
        var frameBevelShadowCorner = Colors.Gray85;

        using (_ = new RenderingTarget(renderer, texture))
        {
            // Highlight: Top and left edges.
            _ = InternalSDL3.SetRenderDrawColor(renderer, frameBevelHighlight);
            foreach (var scaledFRect in new[]
                     {
                         new ScaledFRect { H = bevelSize, W = rect.W - bevelSize, X = rect.X, Y = rect.Y },
                         new ScaledFRect
                             { H = rect.H - (bevelSize * 2), W = bevelSize, X = rect.X, Y = rect.Y + bevelSize }
                     })
            {
                InternalSDL3.RenderFillRect(renderer, scaledFRect);
            }

            // Shadow: bottom and right edges.
            _ = InternalSDL3.SetRenderDrawColor(renderer, frameBevelShadow);
            foreach (var scaledFRect in new[]
                     {
                         new ScaledFRect { H = bevelSize, W = rect.W, X = rect.X, Y = rect.Y + rect.H - bevelSize },
                         new ScaledFRect { H = rect.H, W = bevelSize, X = rect.X + rect.W - bevelSize, Y = rect.Y }
                     })
            {
                InternalSDL3.RenderFillRect(renderer, scaledFRect);
            }

            // Draw white triangle, bottom left.
            _ = InternalSDL3.SetRenderDrawColor(renderer, frameBevelHighlight);
            var vertexListBottomLeft = new List<ScaledVertex>
            {
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = rect.X, Y = rect.H + rect.Y }
                },
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = rect.X, Y = rect.H - bevelSize + rect.Y }
                },
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = bevelSize + rect.X, Y = rect.H - bevelSize + rect.Y }
                }
            };
            _ = InternalSDL3.RenderGeometry(renderer, IntPtr.Zero, vertexListBottomLeft);

            // Draw white triangle, upper right.
            var vertexListUpperRight = new List<ScaledVertex>
            {
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.Y }
                },
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = rect.W - bevelSize + rect.X, Y = rect.Y }
                },
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = rect.W - bevelSize + rect.X, Y = bevelSize + rect.Y }
                }
            };
            _ = InternalSDL3.RenderGeometry(renderer, IntPtr.Zero, vertexListUpperRight);

            // Render a block on the top-left that's going to be black to render triangles on
            _ = InternalSDL3.SetRenderDrawColor(renderer, frameBevelShadow);
            foreach (var scaledFRect in new[]
                     {
                         new ScaledFRect { H = bevelSize + 1, W = bevelSize, X = rect.X, Y = rect.Y },
                         new ScaledFRect { H = bevelSize, W = bevelSize + 1, X = rect.X, Y = rect.Y }
                     })
            {
                InternalSDL3.RenderFillRect(renderer, scaledFRect);
            }

            // Draw white triangles, upper left
            var vertexListUpperLeftA = new List<ScaledVertex>
            {
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = rect.X, Y = 1 + rect.Y }
                },
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = rect.X, Y = bevelSize + 1 + rect.Y }
                },
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = bevelSize + rect.X, Y = bevelSize + 1 + rect.Y }
                }
            };
            _ = InternalSDL3.RenderGeometry(renderer, IntPtr.Zero, vertexListUpperLeftA);

            var vertexListUpperLeftB = new List<ScaledVertex>
            {
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = 1 + rect.X, Y = rect.Y }
                },
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = bevelSize + 1 + rect.X, Y = rect.Y }
                },
                new()
                {
                    Color = frameBevelHighlight.ToFColor(),
                    Position = new ScaledFPoint { X = bevelSize + 1 + rect.X, Y = bevelSize + rect.Y }
                }
            };
            _ = InternalSDL3.RenderGeometry(renderer, IntPtr.Zero, vertexListUpperLeftB);

            // Render a block on the bottom-right that's going to be gray to render triangles on
            _ = InternalSDL3.SetRenderDrawColor(renderer, frameBevelShadowCorner);
            foreach (var scaledFRect in new[]
                     {
                         new ScaledFRect
                         {
                             H = bevelSize + 1, W = bevelSize, X = rect.W - bevelSize + rect.X,
                             Y = rect.H - (bevelSize + 1) + rect.Y
                         },
                         new ScaledFRect
                         {
                             H = bevelSize, W = bevelSize + 1, X = rect.W - (bevelSize + 1) + rect.X,
                             Y = rect.H - bevelSize + rect.Y
                         }
                     })
            {
                InternalSDL3.RenderFillRect(renderer, scaledFRect);
            }

            // Draw black triangles, lower right
            _ = InternalSDL3.SetRenderDrawColor(renderer, frameBevelShadow);
            var vertexListLowerRightA = new List<ScaledVertex>
            {
                new()
                {
                    Color = frameBevelShadow.ToFColor(),
                    Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.H - 1 + rect.Y }
                },
                new()
                {
                    Color = frameBevelShadow.ToFColor(),
                    Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.H - (bevelSize + 1) + rect.Y }
                },
                new()
                {
                    Color = frameBevelShadow.ToFColor(),
                    Position = new ScaledFPoint
                        { X = rect.W - bevelSize + rect.X, Y = rect.H - (bevelSize + 1) + rect.Y }
                }
            };
            _ = InternalSDL3.RenderGeometry(renderer, IntPtr.Zero, vertexListLowerRightA);

            var vertexListLowerRightB = new List<ScaledVertex>
            {
                new()
                {
                    Color = frameBevelShadow.ToFColor(),
                    Position = new ScaledFPoint { X = rect.W - 1 + rect.X, Y = rect.H + rect.Y }
                },
                new()
                {
                    Color = frameBevelShadow.ToFColor(),
                    Position = new ScaledFPoint { X = rect.W - (bevelSize + 1) + rect.X, Y = rect.H + rect.Y }
                },
                new()
                {
                    Color = frameBevelShadow.ToFColor(),
                    Position = new ScaledFPoint
                        { X = rect.W - (bevelSize + 1) + rect.X, Y = rect.H - bevelSize + rect.Y }
                }
            };
            _ = InternalSDL3.RenderGeometry(renderer, IntPtr.Zero, vertexListLowerRightB);
        }
    }
}
