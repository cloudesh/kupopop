using Kupopop;
using Kupopop.Shared;

internal static class SoundEventChecks
{
    public static void Run(Action<bool, string> check)
    {
        var me = Guid.NewGuid(); var drawer = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var players = new[] { new PlayerView(new(me, "Artist", 1), 0, true, false, false, false) };
        var choosing = new RoomView("ROOM", new("Test"), "choosing", drawer, drawer, Guid.NewGuid(), 1,
            now.AddSeconds(15), "", null, false, players, now);
        var drawing = choosing with { Phase = "drawing", Deadline = now.AddSeconds(90) };
        string[] Cues(RoomView? before, RoomView after) => GameSoundEvents.Transition(before, after, me);
        check(Cues(null, drawing).Length == 0 && Cues(drawing, drawing).Length == 0, "joining and duplicate snapshots stay silent");
        check(Cues(choosing, drawing).SequenceEqual(["next_turn"]), "drawing audio starts after word selection");
        var solved = drawing with { YouAreDone = true, Players = [players[0] with { Solved = true }] };
        check(Cues(drawing, solved).SequenceEqual(["correct_guess"]) && Cues(solved, solved).Length == 0, "correct guess plays once from the confirmed solve");
        var ended = drawing with { Phase = "intermission", ServerTime = drawing.Deadline, Deadline = drawing.Deadline.AddSeconds(6) };
        check(Cues(drawing, ended).SequenceEqual(["no_guesses"]), "unsolved timer expiry plays missed-guess cue");
        check(Cues(drawing, ended with { ServerTime = now.AddSeconds(10) }).Length == 0, "skips and early endings do not play missed-guess cue");
        check(Cues(solved, ended with { Players = solved.Players }).Length == 0, "solved players never receive missed-guess cue");
        check(GameSoundEvents.Transition(drawing with { DrawerId = me }, ended with { DrawerId = me }, me).Length == 0, "drawer never receives missed-guess cue");
        var podium = ended with { Players = solved.Players, FirstGuessers = [new(me, "Artist", 1000)] };
        check(Cues(drawing, podium).SequenceEqual(["correct_guess", "top_3"]), "final solver hears correct guess before podium cue");
        check(Cues(solved, podium).SequenceEqual(["top_3"]) && Cues(podium, podium).Length == 0, "turn podium sounds once for its first three guessers");
        var final = podium with { Phase = "results", MatchResults = [new(me, "Artist", 2000)] };
        check(Cues(podium, final).SequenceEqual(["top_3"]) && Cues(final, final).Length == 0, "match podium sounds once for a top-three finish");
        check(Cues(podium, final with { MatchResults = [] }).Length == 0, "unranked match finish stays silent");
        check(Cues(drawing, solved with { Code = "OTHER" }).Length == 0 &&
            Cues(drawing, solved with { TurnId = Guid.NewGuid() }).Length == 0, "room and turn changes do not invent correct guesses");
        check(Cues(choosing, drawing with { Players = [] }).Length == 0, "spectator without membership receives no game cues");
    }
}
