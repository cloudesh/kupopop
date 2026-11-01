namespace Kupopop.Shared;

public sealed record AvatarPreset(string Id, string Name, int Seed = 0);

public static class AvatarPresets
{
    public static readonly AvatarPreset[] Items =
    [
        new("moogle-1", "Moogle / Pom One", 123), new("moogle-2", "Moogle / Pom Two", 456),
        new("moogle-3", "Moogle / Pom Three", 789), new("moogle-4", "Moogle / Pom Four", 99),
        new("moogle-5", "Moogle / Pom Five", 246), new("moogle-6", "Moogle / Pom Six", 357),
        new("moogle-7", "Moogle / Pom Seven", 680),
    ];
    public static AvatarPreset? Find(string? id) => Items.FirstOrDefault(p => p.Id == id);
    public static byte[] Pixels(PlayerProfile profile) => profile.AvatarPixels ??
        AvatarGenerator.Generate(Find(profile.AvatarPreset)?.Seed ?? profile.AvatarSeed);
}
