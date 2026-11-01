using System.Collections.Concurrent;
using Kupopop.Shared;

namespace Kupopop;

/// <summary>Opt-in, short HTTPS checks. Never creates a game socket or room session.</summary>
public sealed class BackgroundInvitations(string directory, DiagnosticLog log) : IDisposable
{
    private readonly CredentialVault vault = new(directory);
    private readonly ConcurrentQueue<(Guid Account, InvitationInbox Inbox)> completed = new();
    private readonly Dictionary<Guid, DateTimeOffset> notified = [];
    private RemoteClient? checking;
    private long nextCheck;
    private Guid account;
    private volatile bool disposed;

    public void Tick(bool enabled, bool windowOpen, Action<GameInvitation> notify, Action disabled)
    {
        if (disposed) return;
        if (!enabled || windowOpen)
        {
            checking?.Dispose(); checking = null;
            while (completed.TryDequeue(out _)) { }
            return;
        }
        while (completed.TryDequeue(out var result))
        {
            if (result.Account != account) continue;
            if (!result.Inbox.Enabled) { disabled(); return; }
            foreach (var id in notified.Where(p => p.Value < DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) notified.Remove(id);
            int shown = 0;
            foreach (var invitation in result.Inbox.Invitations.Where(i => i.ExpiresAt > DateTimeOffset.UtcNow))
            {
                if (notified.Count >= 128) notified.Remove(notified.MinBy(p => p.Value).Key);
                if (notified.TryAdd(invitation.Id, invitation.ExpiresAt) && shown++ < 3) notify(invitation);
            }
        }
        if (Environment.TickCount64 < nextCheck) return;
        nextCheck = Environment.TickCount64 + 60000;
        var endpoint = new Uri(OfficialService.Origin);
        DeviceAccount? saved;
        try { saved = vault.Load(endpoint); }
        catch (Exception ex) { log.Write("invitation.credentials_unavailable", ex); return; }
        if (saved is null) return;
        if (account != saved.AccountId) { account = saved.AccountId; notified.Clear(); }
        var active = new RemoteClient(endpoint);
        checking = active;
        _ = Task.Run(async () =>
        {
            try
            {
                var inbox = await active.CheckInvitationsAsync(saved.AccessToken).ConfigureAwait(false);
                if (!disposed) completed.Enqueue((saved.AccountId, inbox));
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException or ObjectDisposedException)
            { /* Outages stay silent while the window is closed; the next check is bounded. */ }
            finally { active.Dispose(); }
        });
    }

    public void Dispose()
    {
        disposed = true; checking?.Dispose();
        while (completed.TryDequeue(out _)) { }
        notified.Clear();
    }
}
