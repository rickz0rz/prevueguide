using Microsoft.Extensions.Logging;

namespace PrevueGuide.Core.Logging;

public class ContainedLineLoggerProvider : ILoggerProvider
{
    private static ContainedLineLogger? _loggerSingleton;
    private static Object _containedLineLoggerLock = new();

    public static ContainedLineLogger Logger
    {
        get
        {
            lock (_containedLineLoggerLock)
            {
                return _loggerSingleton ??= new ContainedLineLogger();
            }
        }
    }

    public void Dispose()
    {
        // throw new NotImplementedException();
    }


    public ILogger CreateLogger(string categoryName)
    {
        return Logger;
    }
}
