using System.Net.WebSockets;
using System.Text.Json;
using Kupopop.Shared;

namespace Kupopop.Server;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this WebApplication app)
    {
        app.MapPost("/api/accounts", CreateAccount).RequireRateLimiting("accounts");
        app.MapPost("/api/recover", RecoverAccount).RequireRateLimiting("accounts");
        app.MapPost("/api/recovery-code", RotateRecoveryCode).RequireRateLimiting("accounts");
        app.MapGet("/api/invitations", ReadInvitations).RequireRateLimiting("invitation-checks");
        app.Map("/ws", RunSocketAsync).RequireRateLimiting("connections");
    }

    private static string? BearerToken(HttpContext context)
    {
        string authorization = context.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : null;
    }

    private static IResult CreateAccount(AccountRequest request, AccountStore accounts)
    {
        try
        {
            var credentials = accounts.Create(request.DisplayName, request.ShareProfile,
                request.TrackStats, request.BackgroundInvitations);
            return Results.Json(credentials, Protocol.Json, statusCode: StatusCodes.Status201Created);
        }
        catch (DisplayNameTakenException)
        {
            return Results.Conflict(new { error = "That display name is unavailable. Please choose another." });
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "Use a display name of 2–40 characters." });
        }
        catch (InvalidOperationException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static IResult ReadInvitations(HttpContext context, GameEngine engine)
    {
        try
        {
            var inbox = engine.InvitationInbox(BearerToken(context));
            return inbox is not null ? Results.Json(inbox, Protocol.Json) : Results.Unauthorized();
        }
        catch (InboxRateLimitException)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }
    }

    private static IResult RecoverAccount(RecoveryRequest request, AccountStore accounts, GameEngine engine)
    {
        if (request.RecoveryCode is null) return Results.BadRequest();
        var credentials = accounts.Recover(request.RecoveryCode);
        if (credentials is null) return Results.Unauthorized();
        engine.Revoke(credentials.AccountId);
        return Results.Json(credentials, Protocol.Json);
    }

    private static IResult RotateRecoveryCode(HttpContext context, AccountStore accounts, GameEngine engine)
    {
        var credentials = accounts.RotateAuthenticated(BearerToken(context));
        if (credentials is null) return Results.Unauthorized();
        engine.Revoke(credentials.AccountId);
        return Results.Json(credentials, Protocol.Json);
    }

    private static async Task RunSocketAsync(HttpContext context, AccountStore accounts, GameEngine engine,
        AccountInputLimits limits, ILogger<GameEngine> log)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (context.Request.Headers["X-Kupopop-Protocol"] != Protocol.Version.ToString())
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }
        string? token = BearerToken(context);
        var profile = accounts.Authenticate(token);
        if (profile is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var peer = new Peer(profile.Id, socket, limits.For(profile.Id));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        // Recovery can rotate the token during the HTTP upgrade. Recheck it under
        // the engine's session lock before attaching the authenticated identity.
        if (!engine.TryAttach(peer, token!))
        {
            socket.Abort();
            return;
        }
        var pump = peer.PumpAsync(stop.Token);
        // The send pump aborts on exit, waking the receiver if sending fails.
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var bytes = await peer.ReceiveAsync(stop.Token);
                if (bytes is null) break;
                var command = JsonSerializer.Deserialize<ClientCommand>(bytes, Protocol.Json)
                    ?? throw new InvalidDataException("Empty command.");
                engine.Handle(peer, command);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or JsonException)
        {
            log.LogInformation("Connection ended. Category={Category}", ex.GetType().Name);
        }
        catch (Exception ex)
        {
            // Exception messages may contain input. Only record category and code locations.
            log.LogError("Connection handler failed. Category={Category} Stack={Stack}", ex.GetType().Name, ex.StackTrace);
        }
        finally
        {
            engine.Detach(peer, intentional: socket.CloseStatus == WebSocketCloseStatus.NormalClosure);
            peer.Complete();
            stop.Cancel();
            socket.Abort();
            try
            {
                await pump;
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                // Expected when the receive path has closed the session.
            }
        }
    }
}
