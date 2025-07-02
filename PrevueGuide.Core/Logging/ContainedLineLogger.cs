using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace PrevueGuide.Core.Logging;

public class ContainedLineLogger : ILogger
{
    public List<string> Lines { get; }

    private readonly int _maximumLineCount;

    public ContainedLineLogger(int maximumLineCount = 5)
    {
        Lines = [];
        _maximumLineCount = maximumLineCount;
    }

    private void AddLine(string line)
    {
        while (Lines.Count >= _maximumLineCount)
        {
            Lines.RemoveAt(0);
        }
        Lines.Add(line);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        AddLine(formatter(state, exception));
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // throw new NotImplementedException();
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        // throw new NotImplementedException();
        return null;
    }
}
