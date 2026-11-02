using System.Threading.Channels;

namespace Kupopop;

/// <summary>Bounded background writes; routine traces require the debug view.</summary>
public sealed class DiagnosticLog : IDisposable
{
    private sealed record Entry(DateTimeOffset At, string Message, Exception? Error);
    private readonly Channel<Entry> entries = Channel.CreateBounded<Entry>(new BoundedChannelOptions(128)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite,
    });
    private readonly string directory;
    private readonly Task worker;
    private DateTime nextCleanup;
    public volatile bool Verbose;
    internal Task Completion => worker;
    public string FilePath => Path.Combine(directory, $"plugin-{DateTime.UtcNow:yyyyMMdd}.log");

    public DiagnosticLog(string configDirectory)
    {
        directory = Path.Combine(configDirectory, "logs");
        worker = Task.Run(WriteEntriesAsync);
    }

    public void Write(string message, Exception? exception = null)
    {
        if (!Verbose && exception is null && !message.Contains("failed", StringComparison.Ordinal) &&
            message != "plugin.stop" && !message.StartsWith("plugin.start", StringComparison.Ordinal))
        {
            return;
        }

        entries.Writer.TryWrite(new(DateTimeOffset.UtcNow, message, exception));
    }

    private async Task WriteEntriesAsync()
    {
        await foreach (var entry in entries.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(directory);
                if (DateTime.UtcNow >= nextCleanup)
                {
                    foreach (var old in Directory.EnumerateFiles(directory, "plugin-*.log*").Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-7)))
                    {
                        File.Delete(old);
                    }

                    nextCleanup = DateTime.UtcNow.AddHours(1);
                }
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 5 * 1024 * 1024)
                {
                    File.Move(FilePath, FilePath + ".previous", true);
                }

                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"{entry.At:O} {entry.Message}");
                // Never record exception messages: they can contain user input.
                for (var current = entry.Error; current is not null; current = current.InnerException)
                {
                    writer.WriteLine($"ExceptionType={current.GetType().FullName} HResult=0x{current.HResult:X8}");
                    if (current is HttpRequestException request)
                    {
                        writer.WriteLine($"HTTP status={(int?)request.StatusCode}");
                    }

                    if (current is System.Net.WebSockets.WebSocketException socket)
                    {
                        writer.WriteLine($"WebSocketError={socket.WebSocketErrorCode}");
                    }

                    writer.WriteLine(current.StackTrace);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // The bounded worker drains without waiting on disk from the game thread.
    public void Dispose() => entries.Writer.TryComplete();
}
