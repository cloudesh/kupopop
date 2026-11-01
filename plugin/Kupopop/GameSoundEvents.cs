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
        bool justSolved = !before.YouAreDone && solved && before.DrawerId != player;
        if (after.Phase == "intermission")
        {
            if ((after.FirstGuessers ?? []).Take(3).Any(p => p.PlayerId == player))
                return justSolved ? ["correct_guess", "top_3"] : ["top_3"];
            if (!solved && !before.YouAreDone && before.DrawerId != player && after.ServerTime >= before.Deadline)
                return ["no_guesses"];
        }
        return justSolved && after.Phase is "drawing" or "intermission" ? ["correct_guess"] : [];
    }
}
