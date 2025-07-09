using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.Model.Listings;
using PrevueGuide.Core.Model.Listings.Channel;
using PrevueGuide.Core.SDL.Wrappers;
using PrevueGuide.Core.Utilities;

namespace PrevueGuide.Core.SDL.Esquire;

public class EsquireGuideThemeProvider : IGuideThemeProvider
{
    private const int ChannelColumnWidth = 144;
    private const int StandardRowHeight = 56;
    private const int StandardColumnWidth = 172;
    private const int LastColumnWidth = StandardColumnWidth + 36;
    private const int SingleArrowWidth = 16;
    private const int DoubleArrowWidth = 24;
    // Arrow has a margin of 1px from the bevel.
    private nint _renderer;

    private readonly ILogger _logger;
    private readonly FontManager _fontManager;

    public EsquireGuideThemeProvider(ILogger logger)
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

    private string GetLineText(Program program)
    {
        // Hardcode this all to just use the fonts from the map?
        var titleValue = program.Title;

        var title = program.IsMovie
            ? titleValue.Split("\"", StringSplitOptions.RemoveEmptyEntries).First().Split("(").First().Trim()
                .Replace("%", "%%")
            : titleValue.Replace("%", "%%");
        var description = program.Description.Replace("%", "%%");

        var rating = !string.IsNullOrWhiteSpace(program.Rating) ? $" %{program.Rating.Replace("-", "")}%" : "";
        var stereo = program.IsStereo ? " %STEREO%" : string.Empty;
        var closedCaptioning = program.IsClosedCaptioned ? " %CC%" : string.Empty;

        var extraString = program.IsMovie
            ? $"{rating}{description}{stereo}{closedCaptioning}"
            : $"{rating}{stereo}{closedCaptioning}".Trim();
        extraString = string.IsNullOrWhiteSpace(extraString) ? string.Empty : $" {extraString}".TrimEnd();

        var generatedDescription = program.IsMovie
            ? $"\"{title.Trim()}\" ({program.Year}){extraString}"
            : $"{title.Trim()}{extraString}";

        return Font.FormatWithFontTokens(generatedDescription);
    }

    /*
    private string RenderListing(Program program)
    {
        var listingRating = "";
        var listingSubtitled = "";

        var prevueGridFont = _fontManager.FontConfigurations["PrevueGrid"];

        if (!string.IsNullOrWhiteSpace(program.Rating))
        {
            listingRating = prevueGridFont.IconMap.ContainsKey(program.Rating)
                ? $" {prevueGridFont.IconMap[program.Rating].Value}"
                : $" {program.Rating}";
        }

        if (!string.IsNullOrWhiteSpace(program.Close))
        {
            listingSubtitled = prevueGridFont.IconMap.ContainsKey(program.Subtitled)
                ? $" {prevueGridFont.IconMap[program.Subtitled].Value}"
                : $" {program.Subtitled}";
        }

        return program.Category == "Movie"
            ? $"\"{program.Title}\" ({program.Year}) {program.Description}{listingRating}{listingSubtitled}"
            : $"{program.Title}{listingRating}{listingSubtitled}";
    }*/

    public IEnumerable<Texture> GenerateRows(IEnumerable<IListing> listings)
    {
        // Will change this to group by channels.
        foreach (var listing in listings)
        {
            if (listing.GetType() == typeof(ChannelListing))
                yield return GenerateChannelListingTexture((ChannelListing)listing);
            else
                throw new NotImplementedException(listing.GetType().Name);
        }
    }

    private Texture GenerateChannelListingTexture(ChannelListing channelListing)
    {
        var height = StandardRowHeight;

        var leftArrow = ArrowType.None;
        var rightArrow = ArrowType.None;
        var canBePast2Lines = false;
        var lines = 2;
        var width = 0;

        var screenScaledWidth = Configuration.UnscaledDrawableWidth - ChannelColumnWidth - LastColumnWidth;
        var columnsCount = screenScaledWidth / StandardColumnWidth;

        var textureHeight = StandardRowHeight;
        var textureWidth = ChannelColumnWidth + LastColumnWidth + (columnsCount * StandardColumnWidth);

        var lastColumnEndTime = channelListing.FirstColumnStartTime.AddMinutes(30 * (columnsCount + 1));

        var programs = channelListing.Programs
            .OrderBy(p => p.StartTime)
            .Where(p =>
                p.StartTime <= channelListing.FirstColumnStartTime && p.EndTime >= channelListing.FirstColumnStartTime ||
                p.StartTime <= lastColumnEndTime && p.EndTime >= lastColumnEndTime ||
                p.StartTime <= channelListing.FirstColumnStartTime && p.EndTime >= lastColumnEndTime)
            .ToList();

        // Only allow multi-line if there's 1 listing.
        canBePast2Lines = programs.Count() == 1;

        // If we can be past 2 lines, calculate the number of lines. Otherwise, keep it at 2.
        if (canBePast2Lines)
        {
            // Calculate the textureHeight here. We'll have to do some math on the rows by pre-generating
            // the text to display.
        }

        var rowTexture = new Texture(_renderer, textureWidth, textureHeight);
        var previousRowX = 0f;

        // Draw the channel frame.
        using (var channelTexture = new Texture(_renderer, ChannelColumnWidth, textureHeight))
        {
            using (_ = new RenderingTarget(_renderer, channelTexture))
            {
                SDL3.SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 0);
                SDL3.SDL.RenderClear(_renderer);
            }

            Frame.CreateBevelOnTexture(_renderer, channelTexture);

            using (_ = new RenderingTarget(_renderer, rowTexture))
            {
                _ = SDL3.SDL.GetTextureSize(channelTexture.SdlTexture, out var w, out var h);

                var r = new SDL3.SDL.FRect
                {
                    X = 0f,
                    Y = 0f,
                    H = h,
                    W = w
                };

                SDL3.SDL.RenderTexture(_renderer, channelTexture.SdlTexture, IntPtr.Zero, r);

                previousRowX += w;
            }
        }

        // Fix all the column widths.
        foreach (var program in programs)
        {
            if (program.StartTime <= channelListing.FirstColumnStartTime && program.EndTime >= lastColumnEndTime)
            {
                // All 3 columns taken up.
                leftArrow = ArrowType.Single;
                if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(-30))
                {
                    leftArrow = ArrowType.Double;
                }

                rightArrow = ArrowType.Single;
                if (program.EndTime >= lastColumnEndTime.AddMinutes(30))
                {
                    rightArrow = ArrowType.Double;
                }

                width = StandardColumnWidth + StandardColumnWidth + LastColumnWidth;
                // Recalculate the line count
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime && program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(60))
            {
                // First and second columns.
                leftArrow = ArrowType.Single;
                if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(-30))
                {
                    leftArrow = ArrowType.Double;
                }

                width = StandardColumnWidth + StandardColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(30) && program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(60))
            {
                // Second and third columns.
                rightArrow = ArrowType.Single;
                if (program.EndTime >= lastColumnEndTime.AddMinutes(30))
                {
                    rightArrow = ArrowType.Double;
                }

                width = StandardColumnWidth + LastColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime && program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(30))
            {
                // First column.
                leftArrow = ArrowType.Single;
                if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(-30))
                {
                    leftArrow = ArrowType.Double;
                }

                width = StandardColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(30) && program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(60))
            {
                // Second column.
                width = StandardColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(60) && program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(90))
            {
                // Third column.
                rightArrow = ArrowType.Single;
                if (program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(120))
                {
                    rightArrow = ArrowType.Double;
                }

                width = LastColumnWidth;
            }

            // Calculate the height of the texture by determining the number of lines we're going to write.

            using (var programTexture = new Texture(_renderer, width, height))
            {
                using (_ = new RenderingTarget(_renderer, programTexture))
                {
                    // This may change depending on what type of listing we're displaying (movie, sports, etc.)
                    _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Transparent);

                    _ = SDL3.SDL.RenderClear(_renderer);

                    var leftMargin = 0;
                    var rightMargin = 0;

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

                    switch (rightArrow)
                    {
                        case ArrowType.Single:
                        {
                            DrawSingleRightArrow(width);
                            rightMargin = SingleArrowWidth;
                            break;
                        }
                        case ArrowType.Double:
                        {
                            DrawDoubleRightArrow(width);
                            rightMargin = DoubleArrowWidth;
                            break;
                        }
                    }

                    // Draw text on the texture.
                    using (var listingLine = new Texture(GenerateDropShadowText(_renderer, _fontManager["PrevueGrid"],
                               GetLineText(program), Colors.Gray170, Configuration.Scale)))
                    {
                        SDL3.SDL.GetTextureSize(listingLine.SdlTexture, out var w, out var h);
                        var rect = new SDL3.SDL.FRect
                        {
                            X = (5 + leftMargin) * Configuration.Scale,
                            Y = 5 * Configuration.Scale,
                            W = w - ((leftMargin + rightMargin) * Configuration.Scale),
                            H = h
                        };

                        SDL3.SDL.RenderTexture(_renderer, listingLine.SdlTexture, IntPtr.Zero, rect);
                    }

                    // Add a bevel.
                    Frame.CreateBevelOnTexture(_renderer, programTexture);
                }

                // Render the programTexture onto the row texture.
                using (_ = new RenderingTarget(_renderer, rowTexture))
                {
                    SDL3.SDL.GetTextureSize(programTexture.SdlTexture, out var w, out var h);

                    var rect = new SDL3.SDL.FRect
                    {
                        X = previousRowX,
                        Y = 0,
                        W = w,
                        H = h
                    };

                    previousRowX += w;
                    SDL3.SDL.RenderTexture(_renderer, programTexture.SdlTexture, IntPtr.Zero, rect);
                }
            }
        }

        return rowTexture;
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

    private void DrawSingleRightArrow(int width)
    {
        var startPositionBlack = width - SingleArrowWidth - 5;
        // Draw black.
        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Black17);

        var arrowBlackVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack, Y = 5 } // 15, 0
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 15, Y = 30 } // 0, 25
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack, Y = 55 } // 15, 50
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
                Position = new ScaledFPoint { X = startPositionBlack + 1, Y = 7 } // 1, 2
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 14, Y = 30 } // 14, 25
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 1, Y = 53 } // 1, 48
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

    private void DrawDoubleRightArrow(int width)
    {
        DrawSingleRightArrow(width);

        var startPositionBlack = width - DoubleArrowWidth - 5;

        // Draw right arrow black
        var arrowBlackVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 23, Y = 5 } // 23, 0
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 8, Y = 30 } // 8, 25
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 23, Y = 55 } // 23, 50
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

    private static IntPtr GenerateDropShadowText(IntPtr renderer, IntPtr font, string text,
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
