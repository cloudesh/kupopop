namespace Kupopop.Shared;

public sealed record ToastMessage(string Id, string Title, string Message, DateTimeOffset? ExpiresAt);

public sealed class ToastQueue(TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly List<ToastMessage> items = [];
    public void Show(string id, string title, string message, TimeSpan? duration = null)
    {
        items.RemoveAll(t => t.Id == id);
        if (items.Count >= 3) items.RemoveAt(0);
        items.Add(new(id, title, message, duration is { } d ? time.GetUtcNow() + d : null));
    }
    public void Dismiss(string id) => items.RemoveAll(t => t.Id == id);
    public ToastMessage[] Visible()
    {
        items.RemoveAll(t => t.ExpiresAt is { } expiry && expiry <= time.GetUtcNow());
        return items.ToArray();
    }
}
