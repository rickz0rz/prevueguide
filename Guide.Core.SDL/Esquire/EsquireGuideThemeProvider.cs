using System.Runtime.InteropServices;
using Guide.Core.Model.Listings;
using Guide.Core.Model.Listings.Channel;
using Guide.Core.SDL.Wrappers;
using Guide.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Guide.Core.SDL.Esquire;

// Note:
// The arrows look thin because of how they're rendering. The top is 1 pixel
// when it should really be like 2-3 ... so maybe i can move it over a little
// and draw a rect?
// How do I handle the time being drawn? Ugh. Pass in the guide texture to a "draw time" method? Return the entire time bar?
// Make the time bar generate as a default set of value if it hasn't run yet (like when the guide is starting up to emulate how it
// looks on ESQ)
// Draw time bar last. When the time is refreshing, re-draw the time onto the time bar. When that's happening, or when the time bar is updating with new half hour blocks,
// always redraw the time clock frame.

public class EsquireGuideThemeProvider : IGuideThemeProvider, IDisposable
{
    private const string EsquireGuideFontName = "PrevueGrid";

    private const int ChannelColumnWidth = 144;
    private const int StandardColumnWidth = 172;
    private const int LastColumnWidth = StandardColumnWidth + 36;
    private const int SingleArrowWidth = 16;
    private const int DoubleArrowWidth = 24;
    private const int BevelMargin = Frame.BevelSize + 1;

    public int GuideHeight => 175;
    public int StandardRowHeight => 56;
    public int TimeBarHeight => 34;

    private nint _renderer;

    private readonly ILogger _logger;
    private readonly FontManager _fontManager;
    private FontSizeManager _fontSizeManager;

    public EsquireGuideThemeProvider(ILogger logger)
    {
        _logger = logger;
        _fontManager = new FontManager(logger);
        _fontSizeManager = new FontSizeManager(_fontManager[EsquireGuideFontName]);
    }

    public void SetRenderer(nint renderer)
    {
        _renderer = renderer;
    }

    public Texture GetTimeBarTexture()
    {
        return GenerateTimeBarListingTexture(new TimeBarListing(DateTime.Now));
    }

    public SDL3.SDL.Color DefaultGuideBackground => Colors.Blue48;
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
        _logger.LogInformation("Generating rows");

        // Will change this to group by channels.
        foreach (var listing in listings)
        {
            if (listing.GetType() == typeof(ChannelListing))
                yield return GenerateChannelListingTexture((ChannelListing)listing);
            else if (listing.GetType() == typeof(TimeBarListing))
                yield return GenerateTimeBarListingTexture((TimeBarListing)listing);
            else if (listing.GetType() == typeof(ImageListing))
                yield return GenerateImageListing((ImageListing)listing);
            else
                throw new NotImplementedException(listing.GetType().Name);
        }
    }

    private Texture GenerateChannelListingTexture(ChannelListing channelListing)
    {
        var screenScaledWidth = Configuration.UnscaledDrawableWidth - ChannelColumnWidth - LastColumnWidth;
        var columnsCount = screenScaledWidth / StandardColumnWidth;
        var textureWidth = ChannelColumnWidth + LastColumnWidth + (columnsCount * StandardColumnWidth);

        var lastColumnEndTime = channelListing.FirstColumnStartTime.AddMinutes(30 * (columnsCount + 1));
        var programs = channelListing.Programs
            .OrderBy(p => p.StartTime)
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

            var lines = textLines.Count;
            var height = (lines < 2 ? 2 : lines) * 24 + 8;
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
                               _fontManager[EsquireGuideFontName], string.IsNullOrWhiteSpace(textLine) ? " " : textLine, Colors.Gray170)))
                    {
                        var computedLeftMargin = lineNumber < 2 ? leftMargin : 0;
                        var rect = new SDL3.SDL.FRect
                        {
                            X = (BevelMargin + computedLeftMargin) * Configuration.Scale,
                            Y = (BevelMargin + yOffset) * Configuration.Scale,
                            W = listingLine.Size.w,
                            H = listingLine.Size.h
                        };

                        _ = SDL3.SDL.RenderTexture(_renderer, listingLine.SdlTexture, IntPtr.Zero, rect);
                    }

                    yOffset += _fontManager.FontConfigurations[EsquireGuideFontName].PointSize - Configuration.Scale;
                    lineNumber++;
                }

                Frame.CreateBevelOnTexture(_renderer, programTexture);
            }

            programTextures.Add(programTexture);
        }

        var maximumTextureHeight = programTextures.Max(programTexture => programTexture.Size.h) / Configuration.Scale;

        var rowTexture = new Texture(_renderer, textureWidth, (int)maximumTextureHeight);

        using (_ = new RenderingTarget(_renderer, rowTexture))
        {
            _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Blue81);
            _ = SDL3.SDL.RenderClear(_renderer);
        }

        // Draw the channel frame.
        var unscaledMaximumTextureHeight = (int)maximumTextureHeight;
        var nextXPosition = DrawChannelInformation(channelListing, unscaledMaximumTextureHeight, rowTexture);

        foreach (var programTexture in programTextures)
        {
            using (_ = new RenderingTarget(_renderer, rowTexture))
            {
                var fRect = new SDL3.SDL.FRect
                {
                    X = nextXPosition,
                    Y = 0,
                    W = programTexture.Size.w,
                    H = programTexture.Size.h
                };

                _ = SDL3.SDL.RenderTexture(_renderer, programTexture.SdlTexture, IntPtr.Zero, fRect);
                nextXPosition += programTexture.Size.w;
            }
        }

        return rowTexture;
    }

    private Texture GenerateTimeBarListingTexture(TimeBarListing timeBarListing)
    {
        // Fix it so the left and right edges are dark
        // Draw the frames with half hour times
        // Provide mechanism to draw time (even if that's taking the old time and doing away with it by clearing it with
        //   a rect and redrawing a frame...
        // Allow times to 'roll-in' to update the frame times

        var screenScaledWidth = Configuration.UnscaledDrawableWidth - ChannelColumnWidth - LastColumnWidth;
        var columnsCount = screenScaledWidth / StandardColumnWidth;
        var guideTextureWidth = ChannelColumnWidth + LastColumnWidth + (columnsCount * StandardColumnWidth);
        var timeBarTexture = new Texture(_renderer, guideTextureWidth, TimeBarHeight * Configuration.Scale);

        using (_ = new RenderingTarget(_renderer, timeBarTexture))
        {
            _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Blue131);
            _ = SDL3.SDL.RenderClear(_renderer);
        }

        Frame.CreateBevelOnTexture(_renderer, timeBarTexture);

        return timeBarTexture;
    }

    private Texture GenerateImageListing(ImageListing imageListing)
    {
        var screenScaledWidth = Configuration.UnscaledDrawableWidth - ChannelColumnWidth - LastColumnWidth;
        var columnsCount = screenScaledWidth / StandardColumnWidth;
        var guideTextureWidth = ChannelColumnWidth + LastColumnWidth + (columnsCount * StandardColumnWidth);

        using var openedImageTexture = new Texture(_logger, _renderer, imageListing.Filename);
        var imageTexture = new Texture(_renderer, guideTextureWidth, (int)openedImageTexture.Size.h);

        using (_ = new RenderingTarget(_renderer, imageTexture))
        {
            _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Blue81);
            _ = SDL3.SDL.RenderClear(_renderer);

            // Todo: If the image is bigger than the texture width, scale it down to fit. Useful for high-res images.
            // Also maybe cap the height or allow it to be capped?

            var dstRect = new SDL3.SDL.FRect
            {
                X = ((guideTextureWidth - openedImageTexture.Size.w) / 2) * Configuration.Scale,
                Y = 0,
                W = openedImageTexture.Size.w * Configuration.Scale,
                H = openedImageTexture.Size.h * Configuration.Scale
            };

            _ = SDL3.SDL.RenderTexture(_renderer, openedImageTexture.SdlTexture, IntPtr.Zero, dstRect);
        }

        return imageTexture;
    }

    private float DrawChannelInformation(ChannelListing channelListing, int unscaledMaximumTextureHeight,
        Texture rowTexture)
    {
        float nextXPosition;

        using var channelTexture = new Texture(_renderer, ChannelColumnWidth, unscaledMaximumTextureHeight);

        using (_ = new RenderingTarget(_renderer, channelTexture))
        {
            _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Transparent);
            _ = SDL3.SDL.RenderClear(_renderer);

            using var channelLine1 = new Texture(GenerateDropShadowText(_renderer, _fontManager[EsquireGuideFontName],
                channelListing.ChannelNumber, Colors.Yellow));
            using var channelLine2 = new Texture(GenerateDropShadowText(_renderer, _fontManager[EsquireGuideFontName],
                channelListing.CallSign, Colors.Yellow));

            var selectedFont = _fontManager.FontConfigurations[EsquireGuideFontName];

            var xOffset1 = (90 - (channelLine1.Size.w / Configuration.Scale) / 2) - 1;
            var dstRect1 = new SDL3.SDL.FRect
            {
                H = channelLine1.Size.h,
                W = channelLine1.Size.w,
                X = (xOffset1 + selectedFont.XOffset) * Configuration.Scale,
                Y = BevelMargin * Configuration.Scale
            };
            _ = SDL3.SDL.RenderTexture(_renderer, channelLine1.SdlTexture, IntPtr.Zero, dstRect1);

            var xOffset2 = (90 - (channelLine2.Size.w / Configuration.Scale) / 2) - 1;
            var dstRect2 = new SDL3.SDL.FRect
            {
                H = channelLine2.Size.h,
                W = channelLine2.Size.w,
                X = (xOffset2 + selectedFont.XOffset) * Configuration.Scale,
                Y = (selectedFont.PointSize + BevelMargin) * Configuration.Scale
            };
            _ = SDL3.SDL.RenderTexture(_renderer, channelLine2.SdlTexture, IntPtr.Zero, dstRect2);
        }

        Frame.CreateBevelOnTexture(_renderer, channelTexture);

        using (_ = new RenderingTarget(_renderer, rowTexture))
        {
            var fRect = new SDL3.SDL.FRect
            {
                X = 0f,
                Y = 0f,
                H = channelTexture.Size.h,
                W = channelTexture.Size.w
            };

            _ = SDL3.SDL.RenderTexture(_renderer, channelTexture.SdlTexture, IntPtr.Zero, fRect);
            nextXPosition = channelTexture.Size.w;
        }

        return nextXPosition;
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
        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Black17);

        var arrowBlackVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 15 + BevelMargin, Y = BevelMargin }
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = BevelMargin, Y = 25 + BevelMargin }
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 15 + BevelMargin, Y = 50 + BevelMargin }
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowBlackVertices, null);

        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Gray170);

        var arrowGrayVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 14 + BevelMargin, Y = 2 + BevelMargin }
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 2 + BevelMargin, Y = 25 + BevelMargin }
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 14 + BevelMargin, Y = 48 + BevelMargin }
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowGrayVertices, null);
    }

    private void DrawSingleRightArrow(int width)
    {
        var startPosition = width - SingleArrowWidth - BevelMargin;
        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Black17);

        var arrowBlackVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPosition, Y = BevelMargin }
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 15, Y = 25 + BevelMargin }
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPosition, Y = 50 + BevelMargin }
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowBlackVertices, null);

        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Gray170);

        var arrowGrayVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 1, Y = 2 +BevelMargin }
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 14, Y = 25 + BevelMargin }
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 1, Y = 48 + BevelMargin }
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowGrayVertices, null);
    }

    private void DrawDoubleLeftArrow()
    {
        DrawSingleLeftArrow();

        var arrowBlackVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 23 + BevelMargin, Y = BevelMargin }
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 8 + BevelMargin, Y = 25 + BevelMargin }
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = 23 + BevelMargin, Y = 50 + BevelMargin }
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowBlackVertices, null);

        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Gray170);

        var arrowGrayVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 22 + BevelMargin, Y = 2 + BevelMargin }
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 10 + BevelMargin, Y = 25 + BevelMargin }
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = 22 + BevelMargin, Y = 48 + BevelMargin }
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowGrayVertices, null);
    }

    private void DrawDoubleRightArrow(int width)
    {
        DrawSingleRightArrow(width);

        var startPosition = width - DoubleArrowWidth - BevelMargin;

        var arrowBlackVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPosition, Y = BevelMargin }
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 15, Y = 25 + BevelMargin }
            },
            new()
            {
                Color = Colors.Black17.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 0, Y = 50 + BevelMargin }
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowBlackVertices, null);

        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Gray170);

        var arrowGrayVertices = new List<ScaledVertex>
        {
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 1, Y = 2 + BevelMargin }
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 14, Y = 25 + BevelMargin }
            },
            new()
            {
                Color = Colors.Gray170.ToFColor(),
                Position = new ScaledFPoint { X = startPosition + 1, Y = 48 + BevelMargin }
            }
        };

        _ = InternalSDL3.RenderGeometry(_renderer, IntPtr.Zero, arrowGrayVertices, null);
    }

    private static IntPtr GenerateDropShadowText(IntPtr renderer, IntPtr font, string text,
        SDL3.SDL.Color fontColor)
    {
        const int horizontalOffset = 1;
        const int verticalOffset = 1;

        _ = SDL3.TTF.SetFontOutline(font, Configuration.Scale);
        using var outlineSurface = new Surface(SDL3.TTF.RenderTextBlended(font, text, 0, Colors.Black17));
        using var outlineTexture = new Texture(SDL3.SDL.CreateTextureFromSurface(renderer, outlineSurface.SdlSurface));

        _ = SDL3.TTF.SetFontOutline(font, 0);
        using var shadowSurface = new Surface(SDL3.TTF.RenderTextBlended(font, text, 0, Colors.Black17));
        using var shadowTexture = new Texture(SDL3.SDL.CreateTextureFromSurface(renderer, shadowSurface.SdlSurface));
        using var mainSurface = new Surface(SDL3.TTF.RenderTextBlended(font, text, 0, fontColor));
        using var mainTexture = new Texture(SDL3.SDL.CreateTextureFromSurface(renderer, mainSurface.SdlSurface));

        var outlineSdlSurface = Marshal.PtrToStructure<SDL3.SDL.Surface>(outlineSurface.SdlSurface);

        // Generate a texture that's 1 * scale wider than the outline, as it's going to
        // first be used to draw the drop shadow at a 1 x 1 offset.
        var resultTexture = SDL3.SDL.CreateTexture(renderer, SDL3.SDL.PixelFormat.RGBA8888,
            SDL3.SDL.TextureAccess.Target, (outlineSdlSurface.Width + 1 * Configuration.Scale),
            outlineSdlSurface.Height + 1 * Configuration.Scale);
        _ = SDL3.SDL.SetTextureBlendMode(resultTexture, SDL3.SDL.BlendMode.Blend);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(renderer, resultTexture))
        {
            _ = SDL3.SDL.SetRenderDrawColor(renderer, 0, 0, 0, 0);
            _ = SDL3.SDL.RenderClear(renderer);

            // Draw clock, black shadow outline
            var dstRect1 = new SDL3.SDL.FRect
                { H = outlineTexture.Size.h, W = outlineTexture.Size.w, X = horizontalOffset * Configuration.Scale, Y = verticalOffset * Configuration.Scale };
            _ = SDL3.SDL.RenderTexture(renderer, outlineTexture.SdlTexture, IntPtr.Zero, dstRect1);

            // Draw clock, black shadow main
            var dstRect2 = new SDL3.SDL.FRect
                { H = shadowTexture.Size.h, W = shadowTexture.Size.w, X = (horizontalOffset + 2) * Configuration.Scale, Y = (verticalOffset + 2) * Configuration.Scale };
            _ = SDL3.SDL.RenderTexture(renderer, shadowTexture.SdlTexture, IntPtr.Zero, dstRect2);

            // Draw clock, black outline
            var dstRect3 = new SDL3.SDL.FRect
                { H = outlineTexture.Size.h, W = outlineTexture.Size.w, X = (horizontalOffset - 1) * Configuration.Scale, Y = (verticalOffset - 1) * Configuration.Scale };
            _ = SDL3.SDL.RenderTexture(renderer, outlineTexture.SdlTexture, IntPtr.Zero, dstRect3);

            // Draw clock, main without outline
            var dstRect4 = new SDL3.SDL.FRect
                { H = mainTexture.Size.h, W = mainTexture.Size.w, X = horizontalOffset * Configuration.Scale, Y = verticalOffset * Configuration.Scale };
            _ = SDL3.SDL.RenderTexture(renderer, mainTexture.SdlTexture, IntPtr.Zero, dstRect4);
        }

        return resultTexture;
    }

    public void Dispose()
    {
        _fontSizeManager.Dispose();
        _fontManager.Dispose();
    }
}
