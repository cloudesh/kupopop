using System.Net.WebSockets;
using System.Text.Json;

namespace Kupopop.Shared;

public static class Protocol
{
    public const int Version = 4;
    public const int CanvasWidth = 384;
    public const int CanvasHeight = 384;
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
        int fragments = 0;
        do
        {
            part = await socket.ReceiveAsync(chunk, cancel);
            if (part.MessageType == WebSocketMessageType.Close) return null;
            if (++fragments > 256 || part.MessageType != WebSocketMessageType.Text || buffer.Length + part.Count > maximumBytes)
                throw new InvalidDataException("Message format or size rejected.");
            buffer.Write(chunk, 0, part.Count);
        } while (!part.EndOfMessage);
        return buffer.ToArray();
    }

    public static bool ValidName(string? name) => name is { Length: >= 2 and <= 40 }
        && name.All(c => !char.IsControl(c)) && !string.IsNullOrWhiteSpace(name);

    public static bool ValidAvatar(byte[]? pixels) => pixels is null ||
        (pixels.Length == AvatarSize * AvatarSize && pixels.All(p => p < Palette.Colors.Length));

    public static bool ValidProfile(PlayerProfile? profile) => profile is not null && profile.Id != Guid.Empty &&
        ValidName(profile.DisplayName) && ValidAvatar(profile.AvatarPixels) &&
        (profile.AvatarPreset is null || AvatarPresets.Find(profile.AvatarPreset) is not null);

    public static bool ValidEvent(ServerEvent message)
    {
        static bool Text(string? text, int limit) => text is not null && text.Length <= limit && !text.Any(char.IsControl);
        static bool Board(byte[]? pixels) => pixels is null ||
            (pixels.Length == CanvasWidth * CanvasHeight && pixels.All(p => Palette.Valid(p, false)));
        static bool Cursor(BrushCursor? cursor) => cursor is null ||
            (cursor.X is >= 0 and < CanvasWidth && cursor.Y is >= 0 and < CanvasHeight &&
             cursor.Size is >= 1 and <= 20 && Palette.Valid(cursor.Color, false) && DrawingTools.Valid(cursor.Tool));
        static bool Results(SolveResult[]? results) => results is null ||
            (results.Length <= MaximumPlayers && results.All(r => r is not null && ValidName(r.Name) && r.Points is >= 0 and <= 1_000_000));

        if (!Text(message.Type, 40) || (message.Error is not null && !Text(message.Error, 300)) ||
            !Board(message.Canvas) || !Cursor(message.Cursor) ||
            (message.Profile is not null && !ValidProfile(message.Profile))) return false;
        if (message.Chat is { } chat && (!ValidName(chat.Name) || !Text(chat.Text, 300))) return false;
        if (message.Draw is { } draw && (!DrawingTools.Valid(draw.Tool) || !Palette.Valid(draw.Color, false) ||
            draw.Size is < 1 or > 20 || draw.X1 is < 0 or >= CanvasWidth || draw.Y1 is < 0 or >= CanvasHeight ||
            draw.X2 is < 0 or >= CanvasWidth || draw.Y2 is < 0 or >= CanvasHeight)) return false;
        if (message.Rooms is { } rooms && (rooms.Length > 100 || rooms.Any(r => r is null || !ValidName(r.Title) || !Text(r.Code, 8)))) return false;
        if (message.WordLists is { } lists && (lists.Length > 64 || lists.Any(l => l is null ||
            !Text(l.Id, 40) || !ValidName(l.Name) || !ValidName(l.Category) || l.Count is < 2 or > 2000))) return false;
        if (message.Room is not { } room) return true;
        return room.Options is not null && ValidName(room.Options.Title) && Text(room.Code, 8) &&
            room.Phase is "waiting" or "choosing" or "drawing" or "intermission" or "results" &&
            room.Players is { Length: >= 1 and <= MaximumPlayers } && room.Players.All(p => p is not null &&
                ValidProfile(p.Profile) && p.Score is >= 0 and <= 1_000_000) &&
            Board(room.Canvas) && Cursor(room.Cursor) && Text(room.Hint, 64) && (room.Word is null || Text(room.Word, 64)) &&
            (room.WordChoices is null || room.WordChoices is { Length: >= 1 and <= 8 } && room.WordChoices.All(w => Text(w, 64))) &&
            Results(room.FirstGuessers) && Results(room.MatchResults);
    }
}

public sealed record AccountRequest(string DisplayName);
public sealed record RecoveryRequest(string RecoveryCode);
public sealed record AccountCredentials(Guid AccountId, string AccessToken, string RecoveryCode, PlayerProfile Profile);
public sealed record PlayerProfile(Guid Id, string DisplayName, int AvatarSeed, byte[]? AvatarPixels = null, string? AvatarPreset = null);

public sealed record RoomOptions(string Title, bool IsPublic = false, int TurnSeconds = 90,
    int Rounds = 2, int HintSeconds = 30, int MaxPlayers = 16, string[]? WordLists = null,
    int WordChoiceCount = 4, bool VoteKickEnabled = true, bool VoteSkipEnabled = true,
    bool CustomWordsEnabled = true);

public sealed record WordListInfo(string Id, string Name, int Count, bool DefaultEnabled, bool Complex, string Category = "FFXIV");
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
    bool KickVoteAvailable = true, bool SkipVoteAvailable = true,
    bool CustomWordAvailable = false, SolveResult[]? MatchResults = null);

public sealed record DrawOperation(string Tool, int X1, int Y1, int X2, int Y2, byte Color = 1, int Size = 3, Guid StrokeId = default);
public sealed record ClientCommand(string Type, string? Text = null, RoomOptions? Options = null,
    DrawOperation? Draw = null, Guid TurnId = default, byte[]? Avatar = null, bool ResetAvatar = false, string? AvatarPreset = null,
    BrushCursor? Cursor = null);
public sealed record ServerEvent(string Type, RoomView? Room = null, RoomSummary[]? Rooms = null,
    ChatLine? Chat = null, DrawOperation? Draw = null, Guid? ActorId = null,
    string? Error = null, PlayerProfile? Profile = null, Guid TurnId = default, string? RejectedCommand = null,
    WordListInfo[]? WordLists = null, byte[]? Canvas = null, bool CanUndo = false, BrushCursor? Cursor = null);
