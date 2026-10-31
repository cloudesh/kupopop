using System.Net.WebSockets;
using System.Text.Json;

namespace Kupopop.Shared;

public static class Protocol
{
    public const int Version = 3;
    public const int CanvasWidth = 384;
    public const int CanvasHeight = 288;
    public const int AvatarSize = 64;
    public const int MaximumPlayers = 16;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
    };

    public static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    public static async Task<byte[]?> ReceiveAsync(WebSocket socket, int maximumBytes, CancellationToken cancel)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        WebSocketReceiveResult part;
        do
        {
            part = await socket.ReceiveAsync(chunk, cancel);
            if (part.MessageType == WebSocketMessageType.Close) return null;
            if (part.MessageType != WebSocketMessageType.Text || buffer.Length + part.Count > maximumBytes)
                throw new InvalidDataException("Message format or size rejected.");
            buffer.Write(chunk, 0, part.Count);
        } while (!part.EndOfMessage);
        return buffer.ToArray();
    }

    public static bool ValidName(string? name) => name is { Length: >= 2 and <= 40 }
        && name.All(c => !char.IsControl(c)) && !string.IsNullOrWhiteSpace(name);

    public static bool ValidAvatar(byte[]? pixels) => pixels is null ||
        (pixels.Length == AvatarSize * AvatarSize && pixels.All(p => p < Palette.Colors.Length));
}

public sealed record AccountRequest(string DisplayName);
public sealed record RecoveryRequest(string RecoveryCode);
public sealed record AccountCredentials(Guid AccountId, string AccessToken, string RecoveryCode, PlayerProfile Profile);
public sealed record PlayerProfile(Guid Id, string DisplayName, int AvatarSeed, byte[]? AvatarPixels = null, string? AvatarPreset = null);

public sealed record RoomOptions(string Title, bool IsPublic = false, int TurnSeconds = 90,
    int Rounds = 2, int HintSeconds = 30, int MaxPlayers = 16, string[]? WordLists = null);

public sealed record WordListInfo(string Id, string Name, int Count, bool DefaultEnabled, bool Complex);
public sealed record KickVoteView(Guid Target, int Votes, int Required, bool YouVoted, DateTimeOffset ExpiresAt);
public sealed record SolveResult(Guid PlayerId, string Name, int Points);
public sealed record BrushCursor(int X, int Y, string Tool, byte Color = 1, int Size = 3, bool Visible = true);

public sealed record RoomSummary(string Code, string Title, int Players, int MaxPlayers, string Phase);
public sealed record PlayerView(PlayerProfile Profile, int Score, bool Connected, bool Solved, bool IsHost, bool IsDrawer);
public sealed record ChatLine(Guid? Sender, string Name, string Text, bool Done = false, bool System = false);

public sealed record RoomView(string Code, RoomOptions Options, string Phase, Guid HostId, Guid? DrawerId,
    Guid TurnId, int Round, DateTimeOffset Deadline, string Hint, string? Word, bool YouAreDone,
    PlayerView[] Players, DateTimeOffset ServerTime, byte[]? Canvas = null, bool ResetCanvas = false,
    bool CanUndo = false, KickVoteView? KickVote = null, string[]? WordChoices = null,
    SolveResult[]? FirstGuessers = null, KickVoteView? SkipVote = null, BrushCursor? Cursor = null,
    bool KickVoteAvailable = true, bool SkipVoteAvailable = true);

public sealed record DrawOperation(string Tool, int X1, int Y1, int X2, int Y2, byte Color = 1, int Size = 3, Guid StrokeId = default);
public sealed record ClientCommand(string Type, string? Text = null, RoomOptions? Options = null,
    DrawOperation? Draw = null, Guid TurnId = default, byte[]? Avatar = null, bool ResetAvatar = false, string? AvatarPreset = null,
    BrushCursor? Cursor = null);
public sealed record ServerEvent(string Type, RoomView? Room = null, RoomSummary[]? Rooms = null,
    ChatLine? Chat = null, DrawOperation? Draw = null, Guid? ActorId = null,
    string? Error = null, PlayerProfile? Profile = null, Guid TurnId = default, string? RejectedCommand = null,
    WordListInfo[]? WordLists = null, byte[]? Canvas = null, bool CanUndo = false, BrushCursor? Cursor = null);
