using Kupopop.Shared;

namespace Kupopop.Server;

public sealed class InboxRateLimitException : Exception;

public sealed partial class GameEngine
{
    private sealed record Invitation(GameInvitation View, string RoomCode);
    // Recent partners are private, bounded and session-only. Closing the plugin clears them.
    private readonly Dictionary<Guid, List<Guid>> recentPlayers = [];
    private readonly Dictionary<Guid, List<Invitation>> invitations = [];
    private readonly Dictionary<Guid, DateTimeOffset> nextInvitation = [];
    private readonly Dictionary<Guid, DateTimeOffset> nextInboxCheck = [];
    private DateTimeOffset nextInboxCleanup;

    public InvitationInbox? InvitationInbox(string? accessToken)
    {
        lock (gate)
        {
            if (accounts.Authenticate(accessToken) is not { } profile) return null;
            if (nextInboxCheck.GetValueOrDefault(profile.Id) > Now) throw new InboxRateLimitException();
            nextInboxCheck[profile.Id] = Now.AddSeconds(20);
            if (!accounts.Preferences(profile.Id).BackgroundInvitations) return new(false, []);
            return new(true, LiveInvitations(profile.Id).Select(i => i with { From = i.From with { AvatarPixels = null } }).ToArray());
        }
    }

    private void CleanInvitationInboxes()
    {
        if (Now < nextInboxCleanup) return;
        nextInboxCleanup = Now.AddSeconds(30);
        foreach (var id in invitations.Keys.ToArray())
            if (LiveInvitations(id).Length == 0) invitations.Remove(id);
    }

    private void RememberRoom(Room room)
    {
        foreach (var id in room.Members.Keys.Where(peers.ContainsKey))
        {
            if (!recentPlayers.TryGetValue(id, out var recent)) recentPlayers[id] = recent = [];
            foreach (var other in room.Members.Keys.Where(other => other != id))
            {
                recent.Remove(other); recent.Insert(0, other);
            }
            if (recent.Count > 30) recent.RemoveRange(30, recent.Count - 30);
        }
    }

    private bool Knows(Guid viewer, Guid target) => viewer == target || accounts.Relationships(viewer).Any(r => r.Id == target)
        || recentPlayers.GetValueOrDefault(viewer)?.Contains(target) == true;

    private void SendSocial(IGamePeer peer)
    {
        var relationships = accounts.Relationships(peer.Id);
        var people = relationships.Select(r => new SocialPerson(accounts.Get(r.Id)!, r.Relationship,
            r.Relationship == "friend" && peers.ContainsKey(r.Id),
            r.Relationship == "friend" && accounts.Preferences(r.Id).BackgroundInvitations)).ToList();
        foreach (var id in recentPlayers.GetValueOrDefault(peer.Id) ?? [])
            if (people.All(p => p.Profile.Id != id) && accounts.Get(id) is { } profile)
                people.Add(new(profile, "recent"));
        peer.Send(new("social", Social: new(people.ToArray(), accounts.Preferences(peer.Id), LiveInvitations(peer.Id))));
    }

    private GameInvitation[] LiveInvitations(Guid id)
    {
        if (!invitations.TryGetValue(id, out var inbox)) return [];
        inbox.RemoveAll(i => i.View.ExpiresAt <= Now || !rooms.TryGetValue(i.RoomCode, out var room)
            || !room.Members.ContainsKey(i.View.From.Id)
            || !accounts.Friends(id, i.View.From.Id));
        return inbox.Select(i => i.View).ToArray();
    }

    private void RefreshFriends(Guid id, bool presenceOnly = false)
    {
        foreach (var relation in accounts.Relationships(id))
            if (peers.TryGetValue(relation.Id, out var other))
            {
                if (presenceOnly && relation.Relationship == "friend")
                    other.Send(new("friend-presence", ActorId: id, Online: peers.ContainsKey(id)));
                else if (!presenceOnly) SendSocial(other);
            }
    }

    private void ChangeFriend(IGamePeer peer, Guid target, string action)
    {
        if (!Knows(peer.Id, target)) throw new ArgumentException("Play together before sending a friend request.");
        accounts.ChangeFriend(peer.Id, target, action);
        SendSocial(peer);
        if (peers.TryGetValue(target, out var other)) SendSocial(other);
    }

    private void ViewProfile(IGamePeer peer, Guid target)
    {
        if (!Knows(peer.Id, target) || accounts.Get(target) is not { } profile)
            throw new ArgumentException("Profile unavailable.");
        var privacy = accounts.Preferences(target);
        bool own = target == peer.Id;
        peer.Send(new("shared-profile", SharedProfile: new(profile, own || privacy.ShareProfile,
            own || privacy.ShareProfile ? accounts.Statistics(target) : null, own ? privacy : null)));
    }

    private void Invite(IGamePeer peer, Room? room, Guid target)
    {
        RequireHost(peer, room);
        CleanInvitationInboxes();
        peers.TryGetValue(target, out var receiver);
        if (room!.Members.Count >= room.Options.MaxPlayers || !accounts.Friends(peer.Id, target) ||
            (receiver is null && !accounts.Preferences(target).BackgroundInvitations) || receiver?.RoomCode is not null)
            throw new ArgumentException("Invite a friend who is outside a room and accepts invitations.");
        if (nextInvitation.GetValueOrDefault(peer.Id) > Now) throw new ArgumentException("Wait a moment before sending another invitation.");
        if (invitations.Sum(pair => pair.Value.Count) >= 1024) throw new ArgumentException("The invitation service is busy. Try again shortly.");
        if (!invitations.TryGetValue(target, out var inbox)) invitations[target] = inbox = [];
        LiveInvitations(target);
        if (inbox.Any(i => i.View.From.Id == peer.Id && i.RoomCode == room.Code)) throw new ArgumentException("Your friend already has this invitation.");
        if (inbox.Count >= 20) throw new ArgumentException("Your friend's invitation inbox is full.");
        var invite = new GameInvitation(Guid.NewGuid(), accounts.Get(peer.Id)!, room.Options.Title, Now.AddMinutes(5));
        inbox.Add(new(invite, room.Code));
        nextInvitation[peer.Id] = Now.AddSeconds(3);
        receiver?.Send(new("invitation", Invitation: invite));
        if (receiver is not null) SendSocial(receiver);
        peer.Send(new("notice", Error: "Invitation sent."));
    }

    private void AcceptInvitation(IGamePeer peer, Room? currentRoom, Guid inviteId)
    {
        LiveInvitations(peer.Id);
        var inbox = invitations.GetValueOrDefault(peer.Id);
        var invitation = inbox?.FirstOrDefault(i => i.View.Id == inviteId);
        if (currentRoom is not null || invitation is null || !rooms.TryGetValue(invitation.RoomCode, out var room)
            || room.Members.Count >= room.Options.MaxPlayers)
            throw new ArgumentException("That invitation is no longer available. Leave your current room before joining.");
        // Reuse join validation and room-state publication, under the engine lock.
        Handle(peer, new("join", Text: room.Code));
        if (peer.RoomCode == room.Code) { inbox!.Remove(invitation); SendSocial(peer); }
    }
}
