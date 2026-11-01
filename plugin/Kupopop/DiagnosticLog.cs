namespace Kupopop;

public sealed class DiagnosticLog
{
    private readonly object gate = new();
    private readonly string directory;
    private DateTime nextCleanup;
    public string FilePath => Path.Combine(directory, $"plugin-{DateTime.UtcNow:yyyyMMdd}.log");

    public DiagnosticLog(string configDirectory)
    {
        directory = Path.Combine(configDirectory, "logs");
        Directory.CreateDirectory(directory);
    }

    public void Write(string message, Exception? exception = null)
    {
        lock (gate)
        {
            try
            {
                if (DateTime.UtcNow >= nextCleanup)
                {
                    foreach (var old in Directory.EnumerateFiles(directory, "plugin-*.log*").Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-7)))
                        File.Delete(old);
                    nextCleanup = DateTime.UtcNow.AddHours(1);
                }
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 5 * 1024 * 1024)
                    File.Move(FilePath, FilePath + ".previous", true);
                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"{DateTimeOffset.UtcNow:O} {message}");
                for (var current = exception; current is not null; current = current.InnerException)
                {
                    writer.WriteLine($"ExceptionType={current.GetType().FullName} HResult=0x{current.HResult:X8}");
                    if (current is HttpRequestException request) writer.WriteLine($"HTTP status={(int?)request.StatusCode}");
                    if (current is System.Net.WebSockets.WebSocketException socket) writer.WriteLine($"WebSocketError={socket.WebSocketErrorCode}");
                    writer.WriteLine(current.StackTrace);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
