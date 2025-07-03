using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL.Esquire;

public class EsquireGuideTextureProvider : IGuideTextureProvider
{
    private const int StandardRowHeight = 56;
    private const int StandardColumnWidth = 172;
    private const int FirstColumnWidth = StandardColumnWidth;
    private const int SecondColumnWidth = StandardColumnWidth;
    private const int ThirdColumnWidth = StandardColumnWidth + 36;
    private const int SingleArrowWidth = 16;
    private const int DoubleArrowWidth = 24;
    // Arrow has a margin of 1px from the bevel.
    private nint _renderer;

    private readonly ILogger _logger;
    private readonly FontManager _fontManager;

    public EsquireGuideTextureProvider(ILogger logger)
    {
        _logger = logger;
        _fontManager = new FontManager(logger);
    }

    public void SetRenderer(nint renderer)
    {
        _renderer = renderer;
    }

    public SDL3.SDL.Color DefaultGuideBackground => Colors.DefaultBlue; // Might remove this in favor of a "render background" method.
    public int DefaultWindowWidth => 716;
    public int DefaultWindowHeight => 436;
    public FullscreenMode DefaultFullscreenMode => FullscreenMode.Letterbox;

    private string RenderListing(Listing listing)
    {
        var listingRating = "";
        var listingSubtitled = "";

        var prevueGridFont = _fontManager.FontConfigurations["PrevueGrid"];

        if (!string.IsNullOrWhiteSpace(listing.Rating))
        {
            listingRating = prevueGridFont.IconMap.ContainsKey(listing.Rating)
                ? $" {prevueGridFont.IconMap[listing.Rating].Value}"
                : $" {listing.Rating}";
        }

        if (!string.IsNullOrWhiteSpace(listing.Subtitled))
        {
            listingSubtitled = prevueGridFont.IconMap.ContainsKey(listing.Subtitled)
                ? $" {prevueGridFont.IconMap[listing.Subtitled].Value}"
                : $" {listing.Subtitled}";
        }

        return listing.Category == "Movie"
            ? $"\"{listing.Title}\" ({listing.Year}) {listing.Description}{listingRating}{listingSubtitled}"
            : $"{listing.Title}{listingRating}{listingSubtitled}";
    }

    public Texture GenerateListingTexture(Listing listing, DateTime firstColumnStartTime)
    {
        var height = StandardRowHeight;

        var leftArrow = ArrowType.None;
        var rightArrow = ArrowType.None;
        var canBePast2Lines = false;
        var lines = 2;
        var width = 0;

        var firstColumnEndTime = firstColumnStartTime.AddMinutes(30);
        var secondColumnStartTime = firstColumnEndTime;
        var secondColumnEndTime = firstColumnStartTime.AddMinutes(60);
        var thirdColumnStartTime = secondColumnEndTime;
        var thirdColumnEndTime = firstColumnStartTime.AddMinutes(90);

        if (listing.StartTime <= firstColumnStartTime && listing.EndTime >= thirdColumnEndTime)
        {
            // All 3 columns taken up.
            leftArrow = ArrowType.Single;
            if (listing.StartTime <= firstColumnStartTime.AddMinutes(-30))
            {
                leftArrow = ArrowType.Double;
            }

            rightArrow = ArrowType.Single;
            if (listing.EndTime >= thirdColumnEndTime.AddMinutes(30))
            {
                rightArrow = ArrowType.Double;
            }

            canBePast2Lines = true;
            width = FirstColumnWidth + SecondColumnWidth + ThirdColumnWidth;
            // Recalculate the line count
        }
        else if (listing.StartTime <= firstColumnStartTime && listing.EndTime >= secondColumnEndTime)
        {
            // First and second columns.
            leftArrow = ArrowType.Single;
            if (listing.StartTime <= firstColumnStartTime.AddMinutes(-30))
            {
                leftArrow = ArrowType.Double;
            }
            width = FirstColumnWidth + SecondColumnWidth;
        }
        else if (listing.StartTime <= secondColumnStartTime && listing.EndTime >= thirdColumnStartTime)
        {
            // Second and third columns.
            rightArrow = ArrowType.Single;
            if (listing.EndTime >= thirdColumnEndTime.AddMinutes(30))
            {
                rightArrow = ArrowType.Double;
            }
            width = SecondColumnWidth + ThirdColumnWidth;
        }
        else if (listing.StartTime <= firstColumnStartTime && listing.EndTime >= firstColumnEndTime)
        {
            // First column.
            leftArrow = ArrowType.Single;
            if (listing.StartTime <= firstColumnStartTime.AddMinutes(-30))
            {
                leftArrow = ArrowType.Double;
            }
            width = FirstColumnWidth;
        }
        else if (listing.StartTime <= secondColumnStartTime && listing.EndTime >= secondColumnEndTime)
        {
            // Second column.
            width = SecondColumnWidth;
        }
        else if (listing.StartTime <= thirdColumnStartTime && listing.EndTime >= thirdColumnEndTime)
        {
            // Third column.
            rightArrow = ArrowType.Single;
            if (listing.EndTime >= thirdColumnEndTime.AddMinutes(30))
            {
                rightArrow = ArrowType.Double;
            }
            width = ThirdColumnWidth;
        }

        // Calculate the height of the texture by determining the number of lines we're going to write.

        var texture = new Texture(_renderer, width, height);
        using (_ = new RenderingTarget(_renderer, texture))
        {
            // This may change depending on what type of listing we're displaying (movie, sports, etc.)
            _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Transparent);

            _ = SDL3.SDL.RenderClear(_renderer);

            var leftMargin = 0;

            switch (leftArrow)
            {
                case ArrowType.Single:
                {
                    DrawSingleLeftArrow();
                    leftMargin = SingleArrowWidth;
                    break;
                }
                case ArrowType.Double:
                {
                    DrawDoubleLeftArrow();
                    leftMargin = DoubleArrowWidth;
                    break;
                }
            }

            // Draw text on the texture.
            using (var listingLine = new Texture(GenerateDropShadowText(_renderer, _fontManager["PrevueGrid"],
                       RenderListing(listing), Colors.Gray170, Configuration.Scale)))
            {
                SDL3.SDL.GetTextureSize(listingLine.SdlTexture, out var w, out var h);
                var rect = new SDL3.SDL.FRect
                {
                    X = (5 + leftMargin) * Configuration.Scale,
                    Y = 5 * Configuration.Scale,
                    W = w,
                    H = h
                };

                SDL3.SDL.RenderTexture(_renderer, listingLine.SdlTexture, IntPtr.Zero, rect);
            }

            // Add a bevel.
            Frame.CreateBevelOnTexture(_renderer, texture);
        }

        return texture;
    }

    private void DrawSingleLeftArrow()
    {
        // Draw black.
        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Black17);

        var arrowBlackVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 20, Y = 5 } // 15, 0
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 5, Y = 30 } // 0, 25
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 20, Y = 55 } // 15, 50
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowBlackVertices, null);

        // Draw gray.
        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Gray170);

        var arrowGrayVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 19, Y = 7 } // 14, 2
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 7, Y = 30 } // 2, 25
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 19, Y = 53 } // 14, 48
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowGrayVertices, null);
    }

    private void DrawDoubleLeftArrow()
    {
        DrawSingleLeftArrow();

        // Draw right arrow black
        var arrowBlackVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 28, Y = 5 } // 23, 0
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 13, Y = 30 } // 8, 25
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 28, Y = 55 } // 23, 50
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowBlackVertices, null);

        // Draw gray.
        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Gray170);

        var arrowGrayVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 27, Y = 7 } // 22, 2
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 15, Y = 30 } // 10, 25
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 27, Y = 53 } // 22, 48
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowGrayVertices, null);
    }

    public enum ArrowTypeWidth
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
        using var outlineSurface = new Surface(SDL3.TTF.RenderTextBlended(font, text, 0, Colors.Black17));
        using var outlineTexture = new Texture(SDL3.SDL.CreateTextureFromSurface(renderer, outlineSurface.SdlSurface));

        SDL3.TTF.SetFontOutline(font, 0);
        using var shadowSurface = new Surface(SDL3.TTF.RenderTextBlended(font, text, 0, Colors.Black17));
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
}
