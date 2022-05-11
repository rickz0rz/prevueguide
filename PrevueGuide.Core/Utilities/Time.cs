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

    public static int CalculateBlockNumber(DateTime dateTime, bool clampToFifteenMinutes = true)
    {
        // Resetting this every day makes things complex with calculating columns
        // during date rollovers.
        // Simple math shows (2 ^ 32) / (48 * 365) ~= 245,147 years
        // So, let's just use that.

        // Math.Floor(time.TotalDays) won't work because on the edge of a new day
        // i.e. 23:59:59.9999999 it reads that as a full new day instead of a close fraction
        // of the previous day.

        var time = dateTime - DateTime.UnixEpoch;
        var block = (time.Ticks / 864000000000) * 48 + time.Hours * 2;

        if (clampToFifteenMinutes)
        {
            if (time.Minutes >= 15)
                block += 1;

            if (time.Minutes >= 45)
                block += 1;
        }
        else
        {
            if (time.Minutes >= 30)
                block += 1;
        }

        return (int)block;
    }
}
