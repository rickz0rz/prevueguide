using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL.Esquire;

// TODO: Make highDPI aware render funcs to make scaling easier.
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

    private readonly nint _renderer;
    private readonly int _scale;

    public EsquireGuideTextureProvider(nint renderer, int scale)
    {
        _renderer = renderer;
        _scale = scale;
    }

    public SDL3.SDL.Color DefaultGuideBackground => Colors.DefaultBlue;

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

        var texture = new Texture(_renderer, width, height, _scale);

        var oldRenderTarget = SDL3.SDL.GetRenderTarget(_renderer);
        _ = SDL3.SDL.SetRenderTarget(_renderer, texture.SdlTexture);
        _ = SDL3Temp.SetRenderDrawColor(_renderer, Colors.DefaultBlue);
        _ = SDL3.SDL.RenderClear(_renderer);

        switch (leftArrow)
        {
            case ArrowType.Single:
            {
                // Draw black.
                _ = SDL3Temp.SetRenderDrawColor(_renderer, Colors.Black17);

                var arrowBlackVertices = new List<SDL3.SDL.Vertex>
                {
                    new()
                    {
                        // 15, 0
                        Color = Colors.Black17.ToFColor(),
                        Position = new ScaledFPoint { X = 20, Y = 5, Scale = _scale }.ToFPoint()
                    },
                    new()
                    {
                        // 0, 25
                        Color = Colors.Black17.ToFColor(),
                        Position = new ScaledFPoint { X = 5, Y = 30, Scale = _scale }.ToFPoint()
                    },
                    new()
                    {
                        // 15, 50
                        Color = Colors.Black17.ToFColor(),
                        Position = new ScaledFPoint { X = 20, Y = 55, Scale = _scale }.ToFPoint()
                    }
                };

                _ = SDL3.SDL.RenderGeometry(_renderer, IntPtr.Zero, arrowBlackVertices.ToArray(), arrowBlackVertices.Count, IntPtr.Zero, 0);

                // Draw gray.
                _ = SDL3Temp.SetRenderDrawColor(_renderer, Colors.Gray170);

                var arrowWhiteVertices = new List<SDL3.SDL.Vertex>
                {
                    new()
                    {
                        // 14, 2
                        Color = Colors.Gray170.ToFColor(),
                        Position = new ScaledFPoint { X = 19, Y = 7, Scale = _scale }.ToFPoint()
                    },
                    new()
                    {
                        // 2, 25
                        Color = Colors.Gray170.ToFColor(),
                        Position = new ScaledFPoint { X = 7, Y = 30, Scale = _scale }.ToFPoint()
                    },
                    new()
                    {
                        // 14, 48
                        Color = Colors.Gray170.ToFColor(),
                        Position = new ScaledFPoint { X = 19, Y = 53, Scale = _scale }.ToFPoint()
                    }
                };

                _ = SDL3.SDL.RenderGeometry(_renderer, IntPtr.Zero, arrowWhiteVertices.ToArray(), arrowWhiteVertices.Count, IntPtr.Zero, 0);

                break;
            }
        }

        Frame.CreateBevelOnTexture(_renderer, texture, _scale);

        _ = SDL3.SDL.SetRenderTarget(_renderer, oldRenderTarget);

        return texture;
    }
}
