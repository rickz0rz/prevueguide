﻿using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddFilter("Microsoft", LogLevel.Warning)
        .AddFilter("System", LogLevel.Warning)
        .AddFilter("LoggingConsoleApp.Program", LogLevel.Debug)
        .AddConsole();
});

var logger = loggerFactory.CreateLogger<Program>();

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    if (eventArgs.ExceptionObject is Exception exception)
    {
        logger.LogCritical("Unhandled exception encountered: {message} @ {stackTrace}",
            exception.Message, exception.StackTrace);
    }
    else
    {
        logger.LogCritical("Unhandled exception encountered: {exceptionObject}", eventArgs.ExceptionObject);
    }

    // Cheap but force the logger to flush.
    if (eventArgs.IsTerminating)
        // ReSharper disable once AccessToDisposedClosure
        loggerFactory?.Dispose();
};

new PrevueGuide.Guide(logger).Run();
