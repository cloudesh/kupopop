using Kupopop.Shared;

namespace Kupopop;

/// <summary>Only confirmed transitions in an existing room may trigger local audio.</summary>
internal static class GameSoundEvents
{
    public static string[] Transition(RoomView? before, RoomView after, Guid? player)
    {
        if (before is null || player is null || before.Code != after.Code ||
            !after.Players.Any(p => p.Profile.Id == player)) return [];

        if (after.Phase == "results" && before.Phase != "results")
        {
            var results = after.MatchResults ?? [];
            var own = results.FirstOrDefault(p => p.PlayerId == player);
            return own is not null && results.Count(p => p.Points > own.Points) < 3 ? ["top_3"] : [];
        }
        if (before.TurnId != after.TurnId) return [];
        if (before.Phase == "choosing" && after.Phase == "drawing") return ["next_turn"];
        if (before.Phase != "drawing") return [];
        bool solved = after.Players.Any(p => p.Profile.Id == player && p.Solved);
        // Solved flags are public room state. Compare known members, never chat text,
        // so everyone hears accepted guesses without exposing or parsing answers.
        int newSolvers = after.Players.Count(p => p.Solved &&
            before.Players.Any(previous => previous.Profile.Id == p.Profile.Id && !previous.Solved));
        var cues = Enumerable.Repeat("correct_guess", newSolvers).ToList();
        if (after.Phase == "intermission")
        {
            if ((after.FirstGuessers ?? []).Take(3).Any(p => p.PlayerId == player))
                cues.Add("top_3");
            else if (!solved && !before.YouAreDone && before.DrawerId != player && after.ServerTime >= before.Deadline)
                cues.Add("no_guesses");
        }
        return after.Phase is "drawing" or "intermission" ? cues.ToArray() : [];
    }
}

/// <summary>One tick per displayed second, without replaying missed seconds after a stall.</summary>
internal sealed class DrawingCountdown
{
    private string? roomCode;
    private Guid turn;
    private int lowestSecond = int.MaxValue;

    public bool Update(RoomView? state, DateTimeOffset serverNow)
    {
        if (state?.Phase != "drawing") { Reset(); return false; }
        if (roomCode != state.Code || turn != state.TurnId)
        { Reset(); roomCode = state.Code; turn = state.TurnId; }
        int second = (int)Math.Clamp(Math.Ceiling((state.Deadline - serverNow).TotalSeconds), 0, 300);
        if (second >= lowestSecond) return false;
        lowestSecond = second;
        return second is >= 1 and <= 10;
    }

    public void Reset() { roomCode = null; turn = default; lowestSecond = int.MaxValue; }
}
