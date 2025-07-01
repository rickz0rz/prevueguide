namespace PrevueGuide.Core.Model;

public record FontConfiguration
{
    public string Filename { get; init; }
    public int PointSize { get; init; }
    public int XOffset { get; init; } = 0;
    public int YOffset { get; init; } = 0;
    public Dictionary<string, IconMap> IconMap { get; init; } = new Dictionary<string, IconMap>();
}
