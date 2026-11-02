using System.Security.Cryptography;
using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class GameEngine
{
    private void CreateRoom(IGamePeer peer, Room? room, ClientCommand command)
    {
        if (room is not null)
        {
            throw new ArgumentException("Leave your current room first.");
        }

        if (rooms.Count >= 100)
        {
            throw new ArgumentException("Server room capacity reached.");
        }

        var hostWords = CustomWordLists.Parse(command.CustomWords);
        var options = Validate(command.Options ?? new("A kupo drawing room"), hostWords);
        string code;
        do
        {
            code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        }
        while (rooms.ContainsKey(code));
        room = new(code, peer.Id, options);
        room.HostWords = hostWords;
        room.MonitorId = ++nextMonitorId;
        room.Members[peer.Id] = new(peer.Id);
        rooms[code] = room;
        peer.RoomCode = code;
        State(room, peer, snapshot: true);
        log.LogInformation("Room created. Public={Public} Capacity={Capacity}", options.IsPublic, options.MaxPlayers);
    }

    private void JoinRoom(IGamePeer peer, Room? room, ClientCommand command)
    {
        if (room is not null)
        {
            throw new ArgumentException("Leave your current room first.");
        }

        string requested = command.Text?.Trim() ?? "";
        if (!rooms.TryGetValue(requested, out room))
        {
            throw new ArgumentException("Room not found.");
        }

        if (room.BannedAccounts.Contains(peer.Id))
        {
            throw new ArgumentException("You have been banned from this room.");
        }

        if (room.Members.Count >= room.Options.MaxPlayers)
        {
            throw new ArgumentException("Room is full.");
        }

        bool inProgress = room.Phase is "choosing" or "drawing" or "intermission";
        bool returning = room.MatchScores.ContainsKey(peer.Id);
        if (inProgress && !returning && room.MatchScores.Count >= Protocol.MaximumPlayers)
        {
            throw new ArgumentException("This match already has 16 participants. Join when the next match starts.");
        }

        room.Members[peer.Id] = new(peer.Id)
        {
            Score = inProgress ? room.MatchScores.GetValueOrDefault(peer.Id)?.Points ?? 0 : 0,
            Solved = room.Phase == "drawing" && room.WordViewers.Contains(peer.Id)
        };
        if (inProgress)
        {
            room.MatchScores.TryAdd(peer.Id, new(peer.Id, accounts.Get(peer.Id)!.DisplayName, 0));
        }

        peer.RoomCode = room.Code;
        RememberRoom(room);
        State(room, peer, snapshot: true);
        if (inProgress && !returning)
        {
            peer.Send(new("notice", Error: room.Round < room.Options.Rounds
            ? "Match in progress. Guess now; drawing turns for new players begin next round."
            : "Final round in progress. Guess now; new players draw in the next match."));
        }

        SystemMessage(room, "A player joined the room.");
        BroadcastState(room, except: peer.Id);
    }

    private void LeaveRoom(IGamePeer peer, Room? room, ClientCommand command)
    {
        if (room is not null)
        {
            Remove(room, peer.Id);
        }

        peer.RoomCode = null;
        peer.Send(new("left"));
    }

    private void StartMatch(IGamePeer peer, Room? room, ClientCommand command)
    {
        RequireHost(peer, room);
        if (room!.Phase is not ("waiting" or "results"))
        {
            throw new ArgumentException("A match is already running.");
        }

        var participants = room.Members.Values.Where(m => m.Connected).Select(m => m.Id).ToArray();
        if (participants.Length < 2)
        {
            throw new ArgumentException("At least two connected players are needed.");
        }

        foreach (var member in room.Members.Values)
        {
            member.Score = 0;
            member.Solved = false;
        }
        room.MatchScores = participants.ToDictionary(id => id, id => new SolveResult(id, accounts.Get(id)!.DisplayName, 0));
        room.StatsParticipants = participants.Where(id => accounts.Preferences(id).TrackStats).ToHashSet();
        room.StatsRecorded = false;
        room.Results = [];
        room.TurnOrder = [];
        room.Round = 0;
        room.TurnIndex = -1;
        NextTurn(room);
    }

    private void ConfigureRoom(IGamePeer peer, Room? room, ClientCommand command)
    {
        RequireHost(peer, room);
        if (room!.Phase is not ("waiting" or "results"))
        {
            throw new ArgumentException("Change room settings between matches.");
        }

        var updatedWords = command.CustomWords is null ? room.HostWords : CustomWordLists.Parse(command.CustomWords);
        var updated = Validate(command.Options ?? room.Options, updatedWords);
        if (updated.MaxPlayers < room.Members.Count)
        {
            throw new ArgumentException("Capacity cannot be smaller than the current room.");
        }

        room.Options = updated;
        room.HostWords = updatedWords;
        BroadcastState(room);
        peer.Send(new("room-configured"));
    }

    private void KickPlayer(IGamePeer peer, Room? room, ClientCommand command)
    {
        RequireHost(peer, room);
        if (!Guid.TryParse(command.Text, out var target) || target == peer.Id || !room!.Members.ContainsKey(target))
        {
            throw new ArgumentException("Select another player to remove.");
        }

        room.BannedAccounts.Add(target);
        Remove(room, target);
        if (peers.TryGetValue(target, out var kicked))
        {
            kicked.RoomCode = null;
            SendSocial(kicked);
            kicked.Send(new("left", Error: "The host banned you from this room."));
        }
    }
}
