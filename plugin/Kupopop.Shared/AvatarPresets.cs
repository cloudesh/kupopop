namespace Kupopop.Shared;

public sealed record AvatarPreset(string Id, string Name, int Seed = 0, uint Icon = 0);

public static class AvatarPresets
{
    // Job art is resolved from each player's installed client. Only this small
    // allow-listed identifier is stored/shared; game artwork is never uploaded.
    public static readonly AvatarPreset[] Items =
    [
        new("moogle-1", "Moogle / Pom One", 123), new("moogle-2", "Moogle / Pom Two", 456),
        new("moogle-3", "Moogle / Pom Three", 789), new("moogle-4", "Moogle / Pom Four", 99),
        new("moogle-5", "Moogle / Pom Five", 246), new("moogle-6", "Moogle / Pom Six", 357),
        new("moogle-7", "Moogle / Pom Seven", 680),
        new("job-paladin", "Job / Paladin", Icon: 62119), new("job-monk", "Job / Monk", Icon: 62120),
        new("job-warrior", "Job / Warrior", Icon: 62121), new("job-dragoon", "Job / Dragoon", Icon: 62122),
        new("job-bard", "Job / Bard", Icon: 62123), new("job-white-mage", "Job / White Mage", Icon: 62124),
        new("job-black-mage", "Job / Black Mage", Icon: 62125), new("job-summoner", "Job / Summoner", Icon: 62127),
        new("job-scholar", "Job / Scholar", Icon: 62128), new("job-ninja", "Job / Ninja", Icon: 62130),
        new("job-machinist", "Job / Machinist", Icon: 62131), new("job-dark-knight", "Job / Dark Knight", Icon: 62132),
        new("job-astrologian", "Job / Astrologian", Icon: 62133), new("job-samurai", "Job / Samurai", Icon: 62134),
        new("job-red-mage", "Job / Red Mage", Icon: 62135), new("job-blue-mage", "Job / Blue Mage", Icon: 62136),
        new("job-gunbreaker", "Job / Gunbreaker", Icon: 62137), new("job-dancer", "Job / Dancer", Icon: 62138),
        new("job-reaper", "Job / Reaper", Icon: 62139), new("job-sage", "Job / Sage", Icon: 62140),
        new("job-viper", "Job / Viper", Icon: 62141), new("job-pictomancer", "Job / Pictomancer", Icon: 62142),
    ];
    public static AvatarPreset? Find(string? id) => Items.FirstOrDefault(p => p.Id == id);
    public static byte[] Pixels(PlayerProfile profile) => profile.AvatarPixels ??
        AvatarGenerator.Generate(Find(profile.AvatarPreset)?.Seed ?? profile.AvatarSeed);
}
