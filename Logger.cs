using System.Collections.Concurrent;

namespace EtlNodeEditor;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = "";
    public string? Source { get; set; }
}

public class Logger
{
    private static Logger? _instance;
    public static Logger Instance => _instance ??= new Logger();
    
    private ConcurrentQueue<LogEntry> _logs = new();
    private const int MaxLogEntries = 1000;
    
    private Logger() { }
    
    public void Debug(string message, string? source = null)
    {
        Log(LogLevel.Debug, message, source);
    }
    
    public void Info(string message, string? source = null)
    {
        Log(LogLevel.Info, message, source);
    }
    
    public void Warning(string message, string? source = null)
    {
        Log(LogLevel.Warning, message, source);
    }
    
    public void Error(string message, string? source = null)
    {
        Log(LogLevel.Error, message, source);
    }
    
    private void Log(LogLevel level, string message, string? source)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            Source = source
        };
        
        _logs.Enqueue(entry);
        
        // Trim old logs
        while (_logs.Count > MaxLogEntries)
        {
            _logs.TryDequeue(out _);
        }
        
        // Also log to console with color
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };
        
        var timestamp = entry.Timestamp.ToString("HH:mm:ss");
        var levelStr = level.ToString().ToUpper().PadRight(7);
        var sourceStr = source != null ? $"[{source}] " : "";
        Console.WriteLine($"{timestamp} {levelStr} {sourceStr}{message}");
        
        Console.ForegroundColor = originalColor;
    }
    
    public IEnumerable<LogEntry> GetLogs() => _logs.ToArray();
    
    public void Clear()
    {
        _logs.Clear();
    }
}
