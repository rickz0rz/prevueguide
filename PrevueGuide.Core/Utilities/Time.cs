namespace PrevueGuide.Core.Utilities;

public static class Time
{
    public static DateTime ClampToPreviousHalfHour(DateTime dateTime)
    {
        // Given a time, clamp it to it's last previous "on the 30 or 00" minutes
        // and clear any seconds, microseconds, etc.
        //
        // i.e. 4:35:04.0093583 -> 4:30:00.0000000

        var newDateTime = dateTime;

        // Remove any milli/micro seconds
        newDateTime = newDateTime.AddTicks(0 - (newDateTime.Ticks % 100000000));

        newDateTime = newDateTime.AddSeconds(0 - newDateTime.Second);

        newDateTime = newDateTime.Minute >= 30
            ? newDateTime.AddMinutes(30 - newDateTime.Minute)
            : newDateTime.AddMinutes(0 - newDateTime.Minute);

        return newDateTime;
    }

    public static int CalculateBlockNumber(DateTime dateTime)
    {
        var block = dateTime.Hour * 2;

        if (dateTime.Minute >= 15)
        {
            block += 1;
        }

        if (dateTime.Minute >= 45)
        {
            block += 1;
        }

        return block;
    }
}
