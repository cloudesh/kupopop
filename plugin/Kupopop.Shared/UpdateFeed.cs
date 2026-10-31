using System.Text.Json;

namespace Kupopop.Shared;

public static class UpdateFeed
{
    public static Version? NewerVersion(ReadOnlyMemory<byte> json, Version installed)
    {
        if (json.Length > 65536) throw new InvalidDataException("Update metadata exceeds limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid update metadata.");
        Version? newest = null;
        foreach (var entry in document.RootElement.EnumerateArray())
            if (entry.TryGetProperty("InternalName", out var name) && name.GetString() == "Kupopop" &&
                entry.TryGetProperty("AssemblyVersion", out var version) && Version.TryParse(version.GetString(), out var candidate) &&
                candidate > installed && (newest is null || candidate > newest)) newest = candidate;
        return newest;
    }
}
