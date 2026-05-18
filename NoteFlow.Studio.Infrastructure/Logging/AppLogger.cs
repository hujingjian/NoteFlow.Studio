using System;
using System.Collections.ObjectModel;
using System.IO;
using Serilog;
using Serilog.Events;

namespace NoteFlow.Studio.Infrastructure.Logging
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    public static class AppLogger
    {
        public static string LogDirectory { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        
        private static readonly int MaxMemoryLogs = 1000;
        private static readonly object _lock = new object();

        public static ObservableCollection<LogEntry> MemoryLogs { get; } = new ObservableCollection<LogEntry>();
        public static Action<LogEntry> OnLogReceived;

        static AppLogger()
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(Path.Combine(LogDirectory, "log-.txt"), 
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        public static void Trace(string message) => LogMessage("TRACE", message, LogEventLevel.Verbose);
        public static void Debug(string message) => LogMessage("DEBUG", message, LogEventLevel.Debug);
        public static void Info(string message) => LogMessage("INFO", message, LogEventLevel.Information);
        public static void Warn(string message) => LogMessage("WARN", message, LogEventLevel.Warning);
        public static void Error(string message, Exception ex = null)
        {
            if (ex != null)
                Log.Write(LogEventLevel.Error, ex, message);
            else
                Log.Write(LogEventLevel.Error, message);

            var entryMessage = ex == null ? message : $"{message}\n{ex.Message}\n{ex.StackTrace}";
            AddMemoryLog("ERROR", entryMessage);
        }

        private static void LogMessage(string stringLevel, string message, LogEventLevel serilogLevel)
        {
            Log.Write(serilogLevel, message);
            AddMemoryLog(stringLevel, message);
        }
        
        private static void AddMemoryLog(string level, string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            OnLogReceived?.Invoke(entry);
        }

        public static void AddMemoryLogFromUI(LogEntry entry)
        {
            lock (_lock)
            {
                MemoryLogs.Insert(0, entry);
                if (MemoryLogs.Count > MaxMemoryLogs)
                {
                    MemoryLogs.RemoveAt(MemoryLogs.Count - 1);
                }
            }
        }
        
        public static void ClearMemoryLogs()
        {
            lock (_lock)
            {
                MemoryLogs.Clear();
            }
        }
    }
}
