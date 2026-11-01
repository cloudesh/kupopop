using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Kupopop.Shared;

namespace Kupopop;

public sealed class AvatarTextures : IDisposable
{
    private sealed record Entry(int Seed, string? Preset, byte[]? Source, IDalamudTextureWrap Texture);
    private readonly Dictionary<Guid, Entry> cache = [];

    public IDalamudTextureWrap Get(PlayerProfile profile)
    {
        if (cache.TryGetValue(profile.Id, out var old) && old.Seed == profile.AvatarSeed &&
            old.Preset == profile.AvatarPreset &&
            ((old.Source is null && profile.AvatarPixels is null) ||
             (old.Source is not null && profile.AvatarPixels is not null && old.Source.AsSpan().SequenceEqual(profile.AvatarPixels))))
            return old.Texture;
        var canvas = new PixelCanvas(64, 64, transparency: true);
        canvas.Load(AvatarPresets.Pixels(profile));
        byte[] rgba = canvas.Rgba();
        var texture = Plugin.TextureProvider.CreateFromRaw(RawImageSpecification.Rgba32(64, 64), rgba, "Kupopop avatar");
        old?.Texture.Dispose();
        cache[profile.Id] = new(profile.AvatarSeed, profile.AvatarPreset, profile.AvatarPixels, texture);
        if (cache.Count > 64)
        {
            var remove = cache.Keys.First(id => id != profile.Id);
            cache[remove].Texture.Dispose(); cache.Remove(remove);
        }
        return texture;
    }

    public void Dispose() { foreach (var value in cache.Values) value.Texture.Dispose(); cache.Clear(); }
}
