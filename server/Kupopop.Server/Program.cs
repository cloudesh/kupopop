using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.RateLimiting;
using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

bool proxy = args.Contains("--proxy");
bool testing = args.Contains("--test-http");
string root = Environment.GetEnvironmentVariable("KUPOPOP_ROOT") ?? AppContext.BaseDirectory;
string data = Path.Combine(root, "data");
string logs = Path.Combine(root, "logs");
Directory.CreateDirectory(root);
if (args.Contains("--database-check") || args.Contains("--backup-database"))
{
    using var store = new AccountStore(data);
    int backupOption = Array.IndexOf(args, "--backup-database");
    if (backupOption >= 0)
    {
        if (backupOption + 1 >= args.Length) throw new ArgumentException("Supply a new backup filename.");
        store.Backup(Path.GetFullPath(args[backupOption + 1]));
    }
    store.VerifyIntegrity();
    Console.WriteLine($"SQLite integrity: ok. Accounts: {store.Count()}.");
    return;
}
var builder = WebApplication.CreateBuilder(args.Where(a => a is not ("--proxy" or "--local" or "--trust-local" or "--test-http")).ToArray());
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
builder.Logging.AddProvider(new FileLogProvider(logs));
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 8192;
    options.Limits.MaxConcurrentConnections = 512;
    options.Limits.MaxConcurrentUpgradedConnections = 256;
    int port = int.TryParse(Environment.GetEnvironmentVariable("KUPOPOP_PORT"), out int configured) ? configured : proxy || testing ? 5080 : 7443;
    options.ListenLocalhost(port, listen =>
    {
        if (!proxy && !testing) listen.UseHttps(LocalCertificate.Load(data, args.Contains("--trust-local")));
    });
});
builder.Services.AddSingleton(_ => new AccountStore(data));
builder.Services.AddSingleton(WordBank.Open(data));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<AccountInputLimits>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("connections", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
await using var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
if (proxy) app.UseForwardedHeaders(new ForwardedHeadersOptions
{ ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health", () => Results.Json(new { service = "Kupo Skribbl", version = typeof(GameEngine).Assembly.GetName().Version?.ToString(), protocol = Protocol.Version, storage = "sqlite" }));

app.MapGameEndpoints();

var game = app.Services.GetRequiredService<GameEngine>();
var tickStop = new CancellationTokenSource();
var ticking = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
    try { while (await timer.WaitForNextTickAsync(tickStop.Token)) game.Tick(); }
    catch (OperationCanceledException) { }
    catch (Exception ex) { app.Logger.LogCritical(ex, "Game clock failed; server stopping."); app.Lifetime.StopApplication(); }
});
app.Lifetime.ApplicationStopping.Register(() => tickStop.Cancel());
app.Logger.LogInformation("Kupo Skribbl {Version} starting. Protocol={Protocol} Mode={Mode}", typeof(GameEngine).Assembly.GetName().Version, Protocol.Version,
    testing ? "isolated integration test" : proxy ? "TLS reverse proxy (loopback backend)" : "localhost development TLS");
try { await app.RunAsync(); }
finally
{
    tickStop.Cancel();
    await ticking;
    tickStop.Dispose();
}
