using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Guide.Core.Logging;

public class ContainedLineLogger : ILogger
{
    public List<string> Lines { get; }
    public long LinesLogged => _linesLogged;

    private readonly int _maximumLineCount;

    private long _linesLogged;

    public ContainedLineLogger(int maximumLineCount = 50)
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
        _linesLogged++;
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
