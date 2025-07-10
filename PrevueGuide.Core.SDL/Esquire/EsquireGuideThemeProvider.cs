using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.Model.Listings;
using PrevueGuide.Core.Model.Listings.Channel;
using PrevueGuide.Core.SDL.Wrappers;
using PrevueGuide.Core.Utilities;

namespace PrevueGuide.Core.SDL.Esquire;

public class EsquireGuideThemeProvider : IGuideThemeProvider, IDisposable
{
    private const string PrevueFontName = "PrevueGrid";

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
    private FontSizeManager _fontSizeManager;

    public EsquireGuideThemeProvider(ILogger logger)
    {
        _logger = logger;
        _fontManager = new FontManager(logger);
        _fontSizeManager = new FontSizeManager(_fontManager[PrevueFontName]);
    }

    public void SetRenderer(nint renderer)
    {
        _renderer = renderer;
    }

    public SDL3.SDL.Color DefaultGuideBackground => Colors.DefaultBlue; // Might remove this in favor of a "render background" method.
    public int DefaultWindowWidth => 716;
    public int DefaultWindowHeight => 436;
    public float ScaleRatio => 1f;
    public FullscreenMode DefaultFullscreenMode => FullscreenMode.Letterbox;

    private string GetLineText(Program program)
    {
        var titleValue = program.Title;

        var title = program.IsMovie
            ? titleValue.Split("\"", StringSplitOptions.RemoveEmptyEntries).First().Split("(").First().Trim()
                .Replace("%", "%%")
            : titleValue.Replace("%", "%%");

        var description = program.Description.Replace("%", "%%");

        if (!string.IsNullOrWhiteSpace(description))
        {
            description += " ";
        }

        var rating = !string.IsNullOrWhiteSpace(program.Rating) ? $"%{program.Rating.Replace("-", "")}% " : "";
        var stereo = program.IsStereo ? "%STEREO% " : string.Empty;
        var closedCaptioning = program.IsClosedCaptioned ? "%CC% " : string.Empty;

        var extraString = program.IsMovie
            ? $"{rating}({program.Year}) {description}{stereo}{closedCaptioning}"
            : $"{rating}{stereo}{closedCaptioning}".Trim();
        extraString = string.IsNullOrWhiteSpace(extraString) ? string.Empty : $" {extraString}".TrimEnd();

        var generatedDescription = program.IsMovie
            ? $"\"{title.Trim()}\" {extraString}"
            : $"{title.Trim()} {extraString}";

        return Font.FormatWithFontTokens(generatedDescription);
    }

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
        var lines = 2;

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

        var canBePast2Lines = programs.Count == 1;
        var programTextures = new List<Texture>();

        foreach (var program in programs)
        {
            var leftArrow = ArrowType.None;
            var rightArrow = ArrowType.None;
            var width = 0;

            var leftMargin = 0;
            var rightMargin = 0;

            if (program.StartTime < channelListing.FirstColumnStartTime)
            {
                leftArrow = ArrowType.Single;
                leftMargin = SingleArrowWidth;

                if (program.StartTime < channelListing.FirstColumnStartTime.AddMinutes(-30))
                {
                    leftArrow = ArrowType.Double;
                    leftMargin = DoubleArrowWidth;
                }
            }

            if (program.EndTime > lastColumnEndTime)
            {
                rightArrow = ArrowType.Single;
                rightMargin = SingleArrowWidth;

                if (program.EndTime > lastColumnEndTime.AddMinutes(30))
                {
                    rightArrow = ArrowType.Double;
                    rightMargin = DoubleArrowWidth;
                }
            }

            if (program.StartTime <= channelListing.FirstColumnStartTime && program.EndTime >= lastColumnEndTime)
            {
                width = StandardColumnWidth + StandardColumnWidth + LastColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime &&
                     program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(60))
            {
                width = StandardColumnWidth + StandardColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(30) &&
                     program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(60))
            {
                width = StandardColumnWidth + LastColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime &&
                     program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(30))
            {
                width = StandardColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(30) &&
                     program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(60))
            {
                width = StandardColumnWidth;
            }
            else if (program.StartTime <= channelListing.FirstColumnStartTime.AddMinutes(60) &&
                     program.EndTime >= channelListing.FirstColumnStartTime.AddMinutes(90))
            {
                width = LastColumnWidth;
            }

            // - 8 to account for the bevel
            var textLines = GetTextLines(leftMargin, rightMargin, width - 8, GetLineText(program));

            if (canBePast2Lines && textLines.Count > 2)
            {
                textLines = textLines.Take(6).ToList(); // Take up to 6 lines.
            }
            else
            {
                textLines = textLines.Take(2).ToList();
            }

            lines = textLines.Count;

            height = (lines < 2 ? 2 : lines) * 24 + 8;
            var yOffset = 0;

            var programTexture = new Texture(_renderer, width, height);

            using (_ = new RenderingTarget(_renderer, programTexture))
            {
                // This may change depending on what type of listing we're displaying (movie, sports, etc.)
                _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Transparent);
                _ = SDL3.SDL.RenderClear(_renderer);

                switch (leftArrow)
                {
                    case ArrowType.Single:
                    {
                        DrawSingleLeftArrow();
                        break;
                    }
                    case ArrowType.Double:
                    {
                        DrawDoubleLeftArrow();
                        break;
                    }
                }

                switch (rightArrow)
                {
                    case ArrowType.Single:
                    {
                        DrawSingleRightArrow(width);
                        break;
                    }
                    case ArrowType.Double:
                    {
                        DrawDoubleRightArrow(width);
                        break;
                    }
                }

                var lineNumber = 0;
                foreach (var textLine in textLines)
                {
                    using (var listingLine = new Texture(GenerateDropShadowText(_renderer,
                               _fontManager[PrevueFontName],
                               textLine, Colors.Gray170, Configuration.Scale)))
                    {
                        SDL3.SDL.GetTextureSize(listingLine.SdlTexture, out var w, out var h);

                        var computedLeftMargin = lineNumber < 2 ? leftMargin : 0;
                        var rect = new SDL3.SDL.FRect
                        {
                            X = (5 + computedLeftMargin) * Configuration.Scale,
                            Y = (5 + yOffset) * Configuration.Scale,
                            W = w,
                            H = h
                        };

                        SDL3.SDL.RenderTexture(_renderer, listingLine.SdlTexture, IntPtr.Zero, rect);
                    }

                    yOffset += _fontManager.FontConfigurations[PrevueFontName].PointSize - 1;
                    lineNumber++;
                }

                Frame.CreateBevelOnTexture(_renderer, programTexture);
            }

            programTextures.Add(programTexture);
        }

        var maximumTextureHeight = programTextures.Max(programTexture =>
        {
            SDL3.SDL.GetTextureSize(programTexture.SdlTexture, out var w, out var h);
            return h;
        });

        var rowTexture = new Texture(_renderer, textureWidth, (int)maximumTextureHeight);
        var previousRowX = 0f;

        // Draw the channel frame.
        var unscaledMaximumTextureHeight = (int)maximumTextureHeight / Configuration.Scale;

        using (var channelTexture = new Texture(_renderer, ChannelColumnWidth, unscaledMaximumTextureHeight))
        {
            using (_ = new RenderingTarget(_renderer, channelTexture))
            {
                SDL3.SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 0);
                SDL3.SDL.RenderClear(_renderer);

                using var channelLine1 = new Texture(GenerateDropShadowText(_renderer, _fontManager[PrevueFontName],
                    channelListing.ChannelNumber, Colors.Yellow));
                using var channelLine2 = new Texture(GenerateDropShadowText(_renderer, _fontManager[PrevueFontName],
                    channelListing.CallSign, Colors.Yellow));

                var selectedFont = _fontManager.FontConfigurations[PrevueFontName];

                _ = SDL3.SDL.GetTextureSize(channelLine1.SdlTexture, out var w1, out var h1);
                var wOffset1 = (90 - (w1 / Configuration.Scale) / 2) + 8;
                var dstRect1 = new SDL3.SDL.FRect
                {
                    H = h1,
                    W = w1,
                    X = (wOffset1 + selectedFont.XOffset) * Configuration.Scale,
                    Y = 5 * Configuration.Scale
                };
                _ = SDL3.SDL.RenderTexture(_renderer, channelLine1.SdlTexture, IntPtr.Zero, in dstRect1);

                _ = SDL3.SDL.GetTextureSize(channelLine2.SdlTexture, out var w2, out var h2);
                var wOffset2 = (90 - (w2 / Configuration.Scale) / 2) + 8;
                var dstRect2 = new SDL3.SDL.FRect
                {
                    H = h2,
                    W = w2,
                    X = (wOffset2 + selectedFont.XOffset) * Configuration.Scale,
                    Y = (selectedFont.PointSize + 5) * Configuration.Scale
                };
                _ = SDL3.SDL.RenderTexture(_renderer, channelLine2.SdlTexture, IntPtr.Zero, in dstRect2);
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

        foreach (var programTexture in programTextures)
        {
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

                SDL3.SDL.RenderTexture(_renderer, programTexture.SdlTexture, IntPtr.Zero, rect);
                previousRowX += w;
            }
        }

        return rowTexture;
    }

    private List<string> GetTextLines(int leftMargin, int rightMargin, int columnWidth, string targetString)
    {
        var currentLineLength = 0;
        var currentLineNumber = 0;
        var currentLine = string.Empty;
        var renderedLines = new List<string>();
        var lineWidth = columnWidth - leftMargin - rightMargin;

        foreach (var component in targetString.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var componentLength = component.ToCharArray().Select(c => _fontSizeManager[$"{c}"]).Sum(v => v.width);
            var paddedComponentLength = (string.IsNullOrWhiteSpace(currentLine) ? 0 : _fontSizeManager[' '].width) + componentLength;

            if (currentLineLength + paddedComponentLength > lineWidth)
            {
                if (!string.IsNullOrWhiteSpace(currentLine))
                {
                    renderedLines.Add(currentLine);
                    currentLine = component;
                    currentLineLength = componentLength;

                    currentLineNumber++;
                    lineWidth = currentLineNumber >= 2
                        ? columnWidth
                        : columnWidth - leftMargin - rightMargin;
                }
                else
                {
                    // We have to split the line in the middle somewhere.
                    var chars = component.ToCharArray();
                    var componentSubLength = 0;
                    var chunk = string.Empty;

                    foreach (var targetChar in chars)
                    {
                        var glyphWidth = _fontSizeManager[targetChar].width;
                        var newSubLength = componentSubLength + glyphWidth;

                        if (newSubLength > lineWidth)
                        {
                            renderedLines.Add(chunk);
                            chunk = string.Empty;
                            componentSubLength = 0;

                            currentLineNumber++;
                            lineWidth = currentLineNumber >= 2
                                ? columnWidth
                                : columnWidth - leftMargin - rightMargin;
                        }

                        chunk = $"{chunk}{targetChar}";
                        componentSubLength += glyphWidth;
                    }

                    if (!string.IsNullOrWhiteSpace(chunk))
                    {
                        var padding = string.IsNullOrWhiteSpace(currentLine) ? string.Empty : " ";
                        currentLine = $"{currentLine}{padding}{chunk}";
                        currentLineLength += componentSubLength;
                    }
                }
            }
            else
            {
                var padding = string.IsNullOrWhiteSpace(currentLine) ? string.Empty : " ";
                currentLine = $"{currentLine}{padding}{component}";
                currentLineLength += paddedComponentLength;
            }
        }

        if (!string.IsNullOrWhiteSpace(currentLine))
        {
            renderedLines.Add(currentLine);
        }

        return renderedLines;
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
                Position = new ScaledFPoint { X = startPositionBlack, Y = 5 } // 23, 0
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 15, Y = 30 } // 8, 25
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 0, Y = 55 } // 23, 50
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
                Position = new ScaledFPoint { X = startPositionBlack + 1, Y = 7 } // 22, 2
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 14, Y = 30 } // 10, 25
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPositionBlack + 1, Y = 53 } // 22, 48
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

    public void Dispose()
    {
        _fontSizeManager.Dispose();
        _fontManager.Dispose();
    }
}
