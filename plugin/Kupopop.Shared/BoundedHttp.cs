namespace Kupopop.Shared;

/// <summary>Read an untrusted response body without allocating beyond its limit.</summary>
public static class BoundedHttp
{
    public static async Task<byte[]> ReadAsync(HttpResponseMessage response, int limit, CancellationToken cancel)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Response exceeds its size limit.");
        using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var body = new MemoryStream();
        byte[] buffer = new byte[Math.Min(4096, limit)];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false)) != 0)
        {
            if (body.Length + count > limit) throw new InvalidDataException("Response exceeds its size limit.");
            body.Write(buffer, 0, count);
        }
        return body.ToArray();
    }
}
