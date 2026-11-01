using Kupopop.Shared;

namespace Kupopop;

public sealed class UpdateChecker : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly CancellationTokenSource stop = new();
    public async Task<Version?> CheckAsync(Version current)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await http.GetAsync("https://raw.githubusercontent.com/cloudesh/kupopop/main/plugin/releases/repo.json",
            HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] body = await BoundedHttp.ReadAsync(response, 65536, timeout.Token).ConfigureAwait(false);
        return UpdateFeed.NewerVersion(body, current);
    }
    public void Dispose() { stop.Cancel(); http.Dispose(); stop.Dispose(); }
}
