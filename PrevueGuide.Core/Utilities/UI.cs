namespace PrevueGuide.Core.Utilities;

public static class UI
{
    public static (int column, int offset) CalculateColumnDetails(int listingBlock,
        int firstColumnWidth, int secondColumnWidth) 
    {
        var currentTimeBlock = Time.CalculateBlockNumber(DateTime.UtcNow, false);
        
        var column = 0;
        var columnOffset = 0;
        
        if (listingBlock <= currentTimeBlock)
        {
            column = 1;
        }
        else if (listingBlock - currentTimeBlock == 1)
        {
            column = 2;
            columnOffset += firstColumnWidth;
        }
        else if (listingBlock - currentTimeBlock == 2)
        {
            column = 3;
            columnOffset += firstColumnWidth + secondColumnWidth;
        }

        return (column, columnOffset);
    }
}
