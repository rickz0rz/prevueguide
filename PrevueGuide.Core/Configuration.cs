namespace PrevueGuide.Core;

public static class Configuration
{
    public static int Scale { get; set; } = 1;
    public static int DrawableWidth { get; set; } = 0;
    public static int DrawableHeight { get; set; } = 0;
    public static int RenderedWidth { get; set; } = 0;
    public static int RenderedHeight { get; set; } = 0;
    public static int X { get; set; } = 0;
    public static int Y { get; set; } = 0;
    public static int UnscaledDrawableWidth => DrawableWidth / Scale;
    public static int UnscaledDrawableHeight => DrawableHeight / Scale;
}
