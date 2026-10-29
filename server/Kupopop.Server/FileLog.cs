using System.Text;

namespace Kupopop.Server;

public sealed class FileLogProvider(string directory) : ILoggerProvider
{
    private readonly object gate = new();
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.EnumerateFiles(directory, "server-*.log").Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-7)))
                try { File.Delete(old); } catch (IOException) { }
            string path = Path.Combine(directory, $"server-{DateTime.UtcNow:yyyyMMdd}.log");
            if (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024)
                File.Move(path, path + ".previous", true);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.WriteLine($"{DateTimeOffset.UtcNow:O} [{level}] {category}: {message}");
            // No exception messages: a framework exception could include user-provided data.
            if (exception is not null) writer.WriteLine($"ExceptionType={exception.GetType().FullName}\n{exception.StackTrace}");
        }
    }

    private sealed class FileLogger(FileLogProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (IsEnabled(level)) owner.Write(category, level, formatter(state, exception), exception); }
    }
}
