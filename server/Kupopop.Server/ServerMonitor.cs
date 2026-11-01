using System.Diagnostics;
using System.Text.Json;

namespace Kupopop.Server;

public sealed record RoomStatus(long Number, bool Public, string Phase, int Connected, int Members,
    int Capacity, int Round, int Rounds);
public sealed record ServerStatus(DateTimeOffset UpdatedAt, bool Running, long UptimeSeconds,
    int ConnectedUsers, int LobbyUsers, RoomStatus[] Rooms)
{
    public int ActiveGames => Rooms.Count(r => r.Phase is "choosing" or "drawing" or "intermission");
}

/// <summary>Private local operational counts; never includes player data or join codes.</summary>
public static class ServerMonitor
{
    private const int MaximumBytes = 65536;

    public static void Write(string path, ServerStatus status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(status));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, overwrite: true);
    }

    public static ServerStatus Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Status file is too large.");
        return JsonSerializer.Deserialize<ServerStatus>(stream) ?? throw new InvalidDataException("Status file is empty.");
    }

    public static async Task PublishAsync(GameEngine game, string path, ILogger log, CancellationToken stop)
    {
        var uptime = Stopwatch.StartNew();
        long nextWarning = 0;
        void Publish(bool running)
        {
            try { Write(path, game.GetStatus(uptime.Elapsed, running)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (Environment.TickCount64 < nextWarning) return;
                nextWarning = Environment.TickCount64 + 60000;
                log.LogWarning("Local monitor snapshot unavailable. Category={Category}", ex.GetType().Name);
            }
        }
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            Publish(true);
            while (await timer.WaitForNextTickAsync(stop)) Publish(true);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { Publish(false); }
    }

    public static async Task WatchAsync(string path, bool once)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            do
            {
                if (!Console.IsOutputRedirected) Console.Clear();
                Console.WriteLine("Kupo Skribbl | live server monitor | Ctrl+C to exit");
                try
                {
                    var status = Read(path);
                    bool stale = DateTimeOffset.UtcNow - status.UpdatedAt > TimeSpan.FromSeconds(10);
                    Console.WriteLine($"{(status.Running && !stale ? "RUNNING" : "STOPPED / STALE")} | Updated {status.UpdatedAt:u} | Uptime {TimeSpan.FromSeconds(status.UptimeSeconds):g}");
                    Console.WriteLine($"Connected users: {status.ConnectedUsers} | In menus: {status.LobbyUsers} | Rooms: {status.Rooms.Length} | Games: {status.ActiveGames}");
                    Console.WriteLine();
                    Console.WriteLine("Room     Visibility  Phase          Online  Members  Capacity  Round");
                    foreach (var room in status.Rooms)
                        Console.WriteLine($"{room.Number,-8} {(room.Public ? "Public" : "Private"),-11} {room.Phase,-14} {room.Connected,6} {room.Members,8} {room.Capacity,9}  {room.Round}/{room.Rounds}");
                    if (status.Rooms.Length == 0) Console.WriteLine("No live rooms.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { Console.WriteLine("Waiting for a readable server snapshot..."); }
                if (!once) await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
            } while (!once);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
