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
builder.Services.AddSingleton(new AccountStore(data));
builder.Services.AddSingleton(WordBank.Open(data));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameEngine>();
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
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
if (proxy) app.UseForwardedHeaders(new ForwardedHeadersOptions
{ ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health", () => Results.Json(new { service = "Kupo Skribbl", version = typeof(GameEngine).Assembly.GetName().Version?.ToString(), protocol = Protocol.Version }));

app.MapPost("/api/accounts", (AccountRequest request, AccountStore accounts) =>
{
    try { return Results.Json(accounts.Create(request.DisplayName), Protocol.Json, statusCode: 201); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "Use a display name of 2–40 characters." }); }
    catch (InvalidOperationException) { return Results.StatusCode(503); }
}).RequireRateLimiting("accounts");

app.MapPost("/api/recover", (RecoveryRequest request, AccountStore accounts, GameEngine engine) =>
{
    if (request.RecoveryCode is null) return Results.BadRequest();
    var result = accounts.Recover(request.RecoveryCode);
    if (result is null) return Results.Unauthorized();
    engine.Revoke(result.AccountId);
    return Results.Json(result, Protocol.Json);
}).RequireRateLimiting("accounts");

app.MapPost("/api/recovery-code", (HttpContext context, AccountStore accounts, GameEngine engine) =>
{
    string authorization = context.Request.Headers.Authorization.ToString();
    var profile = authorization.StartsWith("Bearer ", StringComparison.Ordinal)
        ? accounts.Authenticate(authorization[7..]) : null;
    if (profile is null) return Results.Unauthorized();
    var result = accounts.Rotate(profile.Id);
    engine.Revoke(profile.Id);
    return Results.Json(result, Protocol.Json);
}).RequireRateLimiting("accounts");

app.Map("/ws", async (HttpContext context, AccountStore accounts, GameEngine engine, ILogger<GameEngine> log) =>
{
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
    if (context.Request.Headers["X-Kupopop-Protocol"] != Protocol.Version.ToString())
    { context.Response.StatusCode = 426; return; }
    string authorization = context.Request.Headers.Authorization.ToString();
    var profile = authorization.StartsWith("Bearer ", StringComparison.Ordinal)
        ? accounts.Authenticate(authorization[7..]) : null;
    if (profile is null) { context.Response.StatusCode = 401; return; }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var peer = new Peer(profile.Id, socket);
    using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    engine.Attach(peer);
    var pump = peer.PumpAsync(stop.Token);
    // A failed sender must also stop the receiver; otherwise aborted peers can linger.
    _ = pump.ContinueWith(_ => { stop.Cancel(); socket.Abort(); }, TaskScheduler.Default);
    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var bytes = await Protocol.ReceiveAsync(socket, 12000, stop.Token);
            if (bytes is null) break;
            var command = JsonSerializer.Deserialize<ClientCommand>(bytes, Protocol.Json)
                ?? throw new InvalidDataException("Empty command.");
            engine.Handle(peer, command);
        }
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or JsonException)
    { log.LogInformation("Connection ended. Category={Category}", ex.GetType().Name); }
    catch (Exception ex) { log.LogError(ex, "Connection handler failed."); }
    finally
    {
        engine.Detach(peer);
        peer.Complete();
        stop.Cancel();
        socket.Abort();
        try { await pump; } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
    }
}).RequireRateLimiting("connections");

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
await app.RunAsync();
tickStop.Cancel();
await ticking;
tickStop.Dispose();
