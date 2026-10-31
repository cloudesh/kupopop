using Kupopop.Shared;

namespace Kupopop;

public sealed class UpdateChecker : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly CancellationTokenSource stop = new();
    public async Task<Version?> CheckAsync(Version current)
    {
        using var response = await http.GetAsync("https://raw.githubusercontent.com/cloudesh/kupopop/main/plugin/releases/repo.json",
            HttpCompletionOption.ResponseHeadersRead, stop.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(stop.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, stop.Token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > 65536) throw new InvalidDataException("Update metadata exceeds limit.");
            bytes.Write(buffer, 0, count);
        }
        return UpdateFeed.NewerVersion(bytes.ToArray(), current);
    }
    public void Dispose() { stop.Cancel(); http.Dispose(); stop.Dispose(); }
}
