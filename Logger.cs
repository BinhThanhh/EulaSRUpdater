namespace EulaSR;

public class Logger
{
    private readonly bool _enableFileLogging;

    public Logger(bool enableFileLogging = false)
    {
        _enableFileLogging = enableFileLogging;
    }
    
    // Static method để tạo logger chỉ console
    public static Logger ConsoleOnly() => new Logger(false);
    
    // Static method để tạo logger có cả file
    public static Logger WithFile() => new Logger(true);

    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    public void Log(LogLevel level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"[{timestamp}] [{level.ToString().ToUpper()}] {message}";

        // Ghi console với màu sắc
        WriteToConsole(level, logEntry);

        // Chỉ ghi file log nếu được enable
        if (_enableFileLogging)
        {
            WriteToFile(logEntry);
        }
    }

    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);

    public void Error(string message, Exception ex)
    {
        Error($"{message}: {ex.Message}");
        Debug($"Stack trace: {ex.StackTrace}");
    }

    private void WriteToConsole(LogLevel level, string logEntry)
    {
        var originalColor = Console.ForegroundColor;
        
        try
        {
            Console.ForegroundColor = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Debug => ConsoleColor.Gray,
                _ => ConsoleColor.White
            };

            Console.WriteLine(logEntry);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private void WriteToFile(string logEntry)
    {
        try
        {
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            
            var logFile = Path.Combine(logDir, $"EulaSR_{DateTime.Now:yyyyMMdd}.log");
            
            File.AppendAllText(logFile, logEntry + Environment.NewLine);
        }
        catch
        {
            // Bỏ qua lỗi ghi file để không làm crash ứng dụng
        }
    }

    public void LogSeparator()
    {
        var separator = new string('=', 50);
        Log(LogLevel.Info, separator);
    }

    public void LogProgress(string operation, int current, int total)
    {
        var percentage = (current * 100) / total;
        var progressBar = new string('█', percentage / 5) + new string('░', 20 - percentage / 5);
        Log(LogLevel.Info, $"{operation}: [{progressBar}] {percentage}% ({current}/{total})");
    }

    public string GetLogFilePath() 
    {
        if (!_enableFileLogging) return "";
        
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        return Path.Combine(logDir, $"EulaSR_{DateTime.Now:yyyyMMdd}.log");
    }
}
