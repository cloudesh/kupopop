using System.Security.Cryptography;
using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class GameEngine
{
    // Called only while the engine lock is held.
    private void Chat(IGamePeer peer, Room? room, string text)
    {
        if (room is null) throw new ArgumentException("Join a room to chat.");
        text = text.Trim();
        if (text.Length is < 1 or > 300 || text.Any(char.IsControl)) throw new ArgumentException("Messages must be 1–300 characters.");
        var member = room.Members[peer.Id];
        bool done = room.Phase == "drawing" && (room.Drawer == peer.Id || member.Solved);
        if (room.Phase == "drawing" && !done)
        {
            if (Now >= room.Deadline) { EndTurn(room); return; }
            string guess = GuessMatching.Normalize(text);
            if (room.Answers.Contains(guess, StringComparer.Ordinal))
            {
                int rank = room.Solvers.Count + 1;
                member.Solved = true;
                room.WordViewers.Add(peer.Id);
                int award = Math.Max(100, 1000 - (rank - 1) * 60);
                member.Score += award;
                room.Solvers.Add(new(peer.Id, accounts.Get(peer.Id)!.DisplayName, award));
                if (room.Drawer is { } drawerId && room.Members.TryGetValue(drawerId, out var drawer)) drawer.Score += 100;
                foreach (var participant in room.Members.Values)
                    room.MatchScores[participant.Id] = new(participant.Id, accounts.Get(participant.Id)!.DisplayName, participant.Score);
                SystemMessage(room, $"{accounts.Get(peer.Id)!.DisplayName} guessed the word! (+{award})");
                log.LogInformation("Correct guess accepted. Rank={Rank}", rank);
                if (room.Members.Values.Where(m => m.Connected && m.Id != room.Drawer).All(m => m.Solved)) EndTurn(room);
                else BroadcastState(room);
                return;
            }
            if (room.Answers.Any(answer => GuessMatching.IsNear(guess, answer)))
            {
                peer.Send(new("chat", Chat: new(null, "Kupo Skribbl", "So close! Try again.", System: true)));
                return;
            }
        }
        var line = new ChatLine(peer.Id, accounts.Get(peer.Id)!.DisplayName, text, Done: done);
        foreach (var recipient in room.Members.Values.Where(m => m.Connected))
            if (!done || recipient.Solved || recipient.Id == room.Drawer)
                if (peers.TryGetValue(recipient.Id, out var receiver)) receiver.Send(new("chat", Chat: line));
        // Message text and guesses are deliberately never written to logs.
    }

    private void NextTurn(Room room)
    {
        room.Vote = room.SkipVote = null; room.VoteUsed = room.SkipVoteUsed = false; room.History.Clear(); room.Cursor = null;
        // Freeze only the current round's roster. New players can guess now
        // and enter the next round without shifting or extending this one.
        while (true)
        {
            do room.TurnIndex++; while (room.TurnIndex < room.TurnOrder.Count &&
                (!room.Members.TryGetValue(room.TurnOrder[room.TurnIndex], out var member) || !member.Connected));
            if (room.TurnIndex < room.TurnOrder.Count || room.Round >= room.Options.Rounds) break;
            room.Round++;
            room.TurnOrder = room.Members.Values.Where(m => m.Connected).Select(m => m.Id).ToList();
            room.TurnIndex = -1;
        }
        if (room.TurnIndex >= room.TurnOrder.Count || room.Members.Values.Count(m => m.Connected) < 2)
        {
            room.Phase = "results";
            room.Drawer = null;
            room.Deadline = default;
            room.CustomWordAvailable = false;
            room.Results = room.MatchScores.Values.OrderByDescending(s => s.Points).ThenBy(s => s.Name, StringComparer.Ordinal).ToArray();
            if (!room.StatsRecorded && room.TurnIndex >= room.TurnOrder.Count && room.Results.Length >= 2)
            {
                if (room.HostWords.Length == 0) accounts.RecordMatch(room.Results, room.StatsParticipants);
                room.StatsRecorded = true;
            }
            SystemMessage(room, "Match finished! The host can start another match.");
            BroadcastState(room);
            log.LogInformation("Match finished. Players={Players}", room.Members.Count);
            return;
        }
        room.Phase = "choosing";
        room.Drawer = room.TurnOrder[room.TurnIndex];
        room.TurnId = Guid.NewGuid();
        room.DrawingLikes.Clear();
        room.WordViewers.Clear();
        room.Deadline = Now.AddSeconds(15);
        room.Revealed = 0;
        room.Word = "";
        room.Choices = words.Choices(room.Options.WordLists, room.LastWord, room.Options.WordChoiceCount, room.HostWords);
        room.CustomWordAvailable = room.Options.CustomWordsEnabled && (customWordRoll?.Invoke() ?? RandomNumberGenerator.GetInt32(100)) < 10;
        room.Answers = [];
        room.Solvers.Clear(); room.HintOrder = [];
        room.Canvas.Clear();
        foreach (var member in room.Members.Values) member.Solved = false;
        BroadcastState(room, reset: true);
        SystemMessage(room, $"{accounts.Get(room.Drawer.Value)!.DisplayName} is choosing the next word.");
        log.LogInformation("Word selection started. Round={Round} Players={Players}", room.Round, room.Members.Count);
    }

    private void BeginDrawing(Room room, string word, bool custom = false)
    {
        room.Word = room.LastWord = word; room.Choices = [];
        room.CustomWordAvailable = false;
        room.Answers = custom ? [GuessMatching.Normalize(word)] : words.Answers(word, room.Options.WordLists);
        room.HintOrder = Enumerable.Range(0, word.Length).Where(i => char.IsLetterOrDigit(word[i])).ToArray();
        RandomNumberGenerator.Shuffle(room.HintOrder.AsSpan());
        room.Phase = "drawing"; room.Deadline = Now.AddSeconds(room.Options.TurnSeconds); room.Revealed = 0;
        room.WordViewers.Add(room.Drawer!.Value);
        BroadcastState(room);
        log.LogInformation("Drawing turn started. Players={Players}", room.Members.Count);
    }

    private void EndTurn(Room room)
    {
        if (room.Phase is not ("drawing" or "choosing")) return;
        room.Vote = room.SkipVote = null; room.History.Clear(); room.Cursor = null; room.Choices = [];
        room.CustomWordAvailable = false;
        room.Phase = "intermission";
        room.Deadline = Now.AddSeconds(6);
        BroadcastState(room);
    }

}
