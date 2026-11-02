using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class GameEngine
{
    private void UpdatePrivacy(IGamePeer peer, Room? room, ClientCommand command)
    {
        accounts.SetPreferences(peer.Id, command.Enabled, command.Shared, command.ResetAvatar, command.BackgroundInvitations);
        if (command.BackgroundInvitations == false)
        {
            invitations.Remove(peer.Id);
        }

        RefreshFriends(peer.Id);
        if (!command.Enabled || command.ResetAvatar)
        {
            foreach (var activeRoom in rooms.Values)
            {
                activeRoom.StatsParticipants.Remove(peer.Id);
            }
        }

        SendSocial(peer);
        ViewProfile(peer, peer.Id);
    }

    private void UpdateProfile(IGamePeer peer, Room? room, ClientCommand command)
    {
        var profile = accounts.Update(peer.Id, command.Text, command.Avatar, command.ResetAvatar, command.AvatarPreset);
        peer.Send(new("profile", Profile: profile));
        if (room is not null)
        {
            BroadcastState(room);
        }

        RefreshFriends(peer.Id);
    }

    private void SubmitSuggestion(IGamePeer peer, Room? room, ClientCommand command)
    {
        string? category = command.Kind == "word" ? command.Category?.Trim() : null;
        if (command.Kind == "word" && !Protocol.ValidName(category))
        {
            throw new ArgumentException("Choose or enter a category of 2–40 characters.");
        }

        accounts.SubmitSuggestion(peer.Id, command.Kind, category, command.Text, Now);
        peer.Send(new("suggestion-sent"));
    }
}
