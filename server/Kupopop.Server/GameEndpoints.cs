using System.Net.WebSockets;
using System.Text.Json;
using Kupopop.Shared;

namespace Kupopop.Server;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this WebApplication app)
    {
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
            var result = accounts.RotateAuthenticated(authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : null);
            if (result is null) return Results.Unauthorized();
            engine.Revoke(result.AccountId);
            return Results.Json(result, Protocol.Json);
        }).RequireRateLimiting("accounts");

        app.Map("/ws", async (HttpContext context, AccountStore accounts, GameEngine engine, AccountInputLimits limits, ILogger<GameEngine> log) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            if (context.Request.Headers["X-Kupopop-Protocol"] != Protocol.Version.ToString())
            { context.Response.StatusCode = 426; return; }
            string authorization = context.Request.Headers.Authorization.ToString();
            var profile = authorization.StartsWith("Bearer ", StringComparison.Ordinal)
                ? accounts.Authenticate(authorization[7..]) : null;
            if (profile is null) { context.Response.StatusCode = 401; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var peer = new Peer(profile.Id, socket, limits.For(profile.Id));
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            if (!engine.TryAttach(peer, authorization[7..])) { socket.Abort(); return; }
            var pump = peer.PumpAsync(stop.Token);
            // Peer.PumpAsync aborts the socket on exit, waking the receiver on failure.
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
            catch (Exception ex) { log.LogError("Connection handler failed. Category={Category} Stack={Stack}", ex.GetType().Name, ex.StackTrace); }
            finally
            {
                engine.Detach(peer);
                peer.Complete();
                stop.Cancel();
                socket.Abort();
                try { await pump; } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
            }
        }).RequireRateLimiting("connections");


    }
}
