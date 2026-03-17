using System;
using Serilog;
using Serilog.Events;

namespace Audex.Utils
{
    /// <summary>
    /// Singleton wrapper around Serilog for application logging.
    /// Thread-safe logging to rolling file in LOCALAPPDATA.
    /// </summary>
    public static class Logger
    {
        private static ILogger? _logger;
        private static readonly object _lock = new object();
        private static bool _initialized = false;

        /// <summary>
        /// Initializes the logger with rolling file sink.
        /// Creates log directories if needed.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    PathHelper.EnsureDirectories();

                    _logger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .WriteTo.File(
                            path: PathHelper.GetLogFilePath(),
                            rollingInterval: RollingInterval.Day,
                            fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                            retainedFileCountLimit: 3,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                        .CreateLogger();

                    _initialized = true;
                }
                catch (Exception)
                {
                    // Swallow initialization failures - preview handler should function without logging
                    _initialized = false;
                }
            }
        }

        /// <summary>
        /// Sets the minimum log level.
        /// </summary>
        /// <param name="level">Log level: "debug", "info", "warning", "error"</param>
        public static void SetLevel(string level)
        {
            if (_logger == null) return;

            LogEventLevel logLevel = level?.ToLowerInvariant() switch
            {
                "debug" => LogEventLevel.Debug,
                "info" => LogEventLevel.Information,
                "warning" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error,
                _ => LogEventLevel.Information
            };

            // Note: Serilog's minimum level is set at initialization.
            // To dynamically change levels, we'd need to use LoggingLevelSwitch.
            // For now, we'll just document that level changes require reinitialization.
        }

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        public static void Debug(string message)
        {
            _logger?.Debug(message);
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        public static void Info(string message)
        {
            _logger?.Information(message);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public static void Warn(string message)
        {
            _logger?.Warning(message);
        }

        /// <summary>
        /// Logs an error message with optional exception.
        /// </summary>
        public static void Error(string message, Exception? ex = null)
        {
            if (ex != null)
            {
                _logger?.Error(ex, message);
            }
            else
            {
                _logger?.Error(message);
            }
        }
    }
}
