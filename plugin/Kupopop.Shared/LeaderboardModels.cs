namespace Kupopop.Shared;

public sealed record LeaderboardEntry(string Name, int AvatarSeed, string? AvatarPreset,
    int BestScore, long Points, long Games, long Likes, byte[]? AvatarPixels = null);
public sealed record LeaderboardView(string Metric, int Page, LeaderboardEntry[] Entries, bool HasNext,
    bool Named, bool Hidden, bool ShowPortrait);
