using Microsoft.Extensions.Logging;
using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL.Esquire;

// TODO: Make highDPI aware render funcs to make scaling easier.
public class EsquireGuideTextureProvider(ILogger logger) : IGuideTextureProvider
{
    private const int StandardRowHeight = 56;
    private const int StandardColumnWidth = 172;
    private const int FirstColumnWidth = StandardColumnWidth;
    private const int SecondColumnWidth = StandardColumnWidth;
    private const int ThirdColumnWidth = StandardColumnWidth + 36;
    private const int SingleArrowWidth = 16;
    private const int DoubleArrowWidth = 24;
    // Arrow has a margin of 1px from the bevel.

    private readonly ILogger _logger = logger;

    private nint _renderer;

    public void SetRenderer(nint renderer)
    {
        _renderer = renderer;
    }

    public SDL3.SDL.Color DefaultGuideBackground => Colors.DefaultBlue; // Might remove this in favor of a "render background" method.
    public int DefaultWindowWidth => 716;
    public int DefaultWindowHeight => 436;
    public bool FullscreenLetterbox => true;

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

        var oldRenderTarget = SDL3.SDL.GetRenderTarget(_renderer);
        _ = SDL3.SDL.SetRenderTarget(_renderer, texture.SdlTexture);

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
        }

        // Draw text on the texture.

        // Add a bevel.
        Frame.CreateBevelOnTexture(_renderer, texture);

        _ = SDL3.SDL.SetRenderTarget(_renderer, oldRenderTarget);

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
}
