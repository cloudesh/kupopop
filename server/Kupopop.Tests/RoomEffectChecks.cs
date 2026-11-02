using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Extensions.Logging.Abstractions;

static partial class RoundFlowChecks
{
    private static void CheckRoomEffects(string working, Action<bool, string> check)
    {
        using var accounts = new AccountStore(Path.Combine(working, "room-effects"));
        var a = accounts.Create("Effect artist", trackStats: true);
        var b = accounts.Create("Effect guesser", trackStats: true);
        (GameEngine Game, Clock Clock, Peer Artist, Peer Guesser) Begin(RoomEffect effects, bool capable = true, int chance = 0, int ticket = 0)
        {
            var clock = new Clock();
            var artist = new Peer(a.AccountId, capable);
            var guesser = new Peer(b.AccountId, capable);
            var rolls = new Queue<int>([chance, ticket]);
            var game = new GameEngine(accounts, NullLogger<GameEngine>.Instance, clock,
                effectRoll: limit => rolls.TryDequeue(out int roll) ? roll % limit : 0);
            game.Attach(artist);
            game.Attach(guesser);
            game.Handle(artist, new("create", Options: new("Effects test", Rounds: 1, Effects: effects)));
            game.Handle(guesser, new("join", Text: artist.State.Code));
            game.Handle(artist, new("start"));
            game.Handle(artist, new("choose-word", Text: artist.State.WordChoices![0], TurnId: artist.State.TurnId));
            return (game, clock, artist, guesser);
        }

        check(Begin(RoomEffect.All, chance: 20).Artist.State.Effect == RoomEffect.None &&
            Begin(RoomEffect.All, chance: 19).Artist.State.Effect != RoomEffect.None,
            "effect roll uses a strict 20 percent boundary");
        check(Begin(RoomEffect.All, capable: false).Artist.State.Effect == RoomEffect.None,
            "older drawers retain their supported canvas rules");
        check(Begin(RoomEffect.All, ticket: 19).Artist.State.Effect == RoomEffect.MirrorInk &&
            Begin(RoomEffect.All, ticket: 20).Artist.State.Effect == RoomEffect.BorrowedCrayons &&
            Begin(RoomEffect.All, ticket: 50).Artist.State.Effect == RoomEffect.DoublePoints &&
            Begin(RoomEffect.All, ticket: 80).Artist.State.Effect == RoomEffect.StickerStarter,
            "conditional draws use the four advertised weights without rolling Encore");

        var mirror = Begin(RoomEffect.MirrorInk);
        var stroke = new DrawOperation("line", 3, 5, 8, 5, 4, 1, Guid.NewGuid());
        var turn = mirror.Artist.State.TurnId;
        mirror.Game.Handle(mirror.Artist, new("draw", Draw: stroke, TurnId: turn));
        var pair = mirror.Guesser.Events.Where(e => e.Draw is not null).Select(e => e.Draw).ToArray();
        check(pair.SequenceEqual([stroke, RoomEffects.Reflect(stroke)]), "spectators receive both mirror strokes in order");
        mirror.Game.Handle(mirror.Artist, new("undo", TurnId: turn));
        check(mirror.Guesser.Events.Last().Canvas!.All(p => p == 0), "one undo restores both mirrored marks");

        var crayons = Begin(RoomEffect.BorrowedCrayons);
        var trio = crayons.Artist.State.Crayons!;
        turn = crayons.Artist.State.TurnId;
        check(trio.Distinct().Count() == 3 && crayons.Guesser.State.Crayons!.SequenceEqual(trio),
            "every participant sees the same three legal crayon colours");
        crayons.Game.Handle(crayons.Artist, new("draw", Draw: stroke with { Color = 0 }, TurnId: turn));
        check(crayons.Artist.Events.Last().Type == "error", "forged ink outside the palette is rejected");
        crayons.Game.Handle(crayons.Artist, new("draw", Draw: stroke with { Color = trio[0] }, TurnId: turn));
        crayons.Game.Handle(crayons.Artist, new("draw", Draw: stroke with { Tool = "eraser", Color = 0 }, TurnId: turn));
        check(crayons.Guesser.Events.Count(e => e.Draw is not null) == 2, "borrowed crayons permit matching ink and the ordinary eraser");

        var sticker = Begin(RoomEffect.StickerStarter);
        var observed = new PixelCanvas();
        foreach (var e in sticker.Guesser.Events.Where(e => e.Draw is not null))
        {
            observed.Apply(e.Draw!);
        }
        sticker.Game.Handle(sticker.Artist, new("sync"));
        check(observed.Pixels.Any(p => p != 0) && observed.Pixels.SequenceEqual(sticker.Artist.State.Canvas!) &&
            !sticker.Artist.State.CanUndo, "starter doodle is synchronized and outside the artist's undo history");
        sticker.Game.Handle(sticker.Artist, new("draw", Draw: stroke, TurnId: sticker.Artist.State.TurnId));
        sticker.Game.Handle(sticker.Artist, new("undo", TurnId: sticker.Artist.State.TurnId));
        check(observed.Pixels.SequenceEqual(sticker.Guesser.Events.Last().Canvas!), "undo restores the original sticker pixels");

        var party = Begin(RoomEffect.DoublePoints | RoomEffect.Encore);
        turn = party.Artist.State.TurnId;
        party.Game.Handle(party.Guesser, new("chat", Text: party.Artist.State.Word));
        var recap = party.Guesser.State;
        check(recap.Players.Single(p => p.IsDrawer).Score == 200 && recap.FirstGuessers![0].Points == 2000 &&
            recap.CanTossPom && !party.Artist.State.CanTossPom && recap.EncoreUntil == party.Clock.GetUtcNow().AddSeconds(5),
            "full solve doubles both awards and opens a five-second guesser encore");
        party.Game.Handle(party.Artist, new("encore-pom", TurnId: turn));
        party.Game.Handle(party.Guesser, new("encore-pom", TurnId: Guid.NewGuid()));
        party.Game.Handle(party.Guesser, new("encore-pom", TurnId: turn));
        party.Game.Handle(party.Guesser, new("encore-pom", TurnId: turn));
        check(party.Artist.Events.Count(e => e.Type == "encore-pom") == 1, "pom tosses require an eligible guesser, current turn and unused tap");
        party.Game.Detach(party.Guesser);
        var returning = new Peer(party.Guesser.Id, true);
        party.Game.Attach(returning);
        check(returning.State.PomCount == 1 && !returning.State.CanTossPom && returning.State.Canvas!.All(p => p == 0),
            "reconnect preserves poms and tap eligibility separately from the canvas pixels");
        party.Clock.Advance(11);
        party.Game.Tick();
        check(returning.State.Phase == "choosing" && returning.State.EncoreUntil is null && returning.State.PomCount == 0,
            "next turn clears all encore state");
        party.Game.Handle(returning, new("choose-word", Text: returning.State.WordChoices![0], TurnId: returning.State.TurnId));
        party.Clock.Advance(91);
        party.Game.Tick();
        check(party.Artist.State is { Phase: "intermission", EncoreUntil: null }, "timeout does not start an encore");
        party.Clock.Advance(6);
        party.Game.Tick();
        check(party.Artist.State.Phase == "results" && accounts.Statistics(b.AccountId)!.Points == 1000 &&
            accounts.Statistics(a.AccountId)!.Points == 100, "lifetime scores exclude both drawer and guesser bonuses");
    }
}
