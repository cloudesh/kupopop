using System.Text;
using Kupopop.Shared;
using Kupopop.Server;
using Microsoft.Extensions.Logging.Abstractions;

static class ReleasePolishChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        using var accounts = new AccountStore(Path.Combine(working, "release-polish"));
        var a = accounts.Create("Custom host", trackStats: true);
        var b = accounts.Create("Custom viewer", trackStats: true);
        var clock = new Clock(); var game = new GameEngine(accounts, NullLogger<GameEngine>.Instance, clock);
        var host = new Peer(a.AccountId); var viewer = new Peer(b.AccountId);
        game.Attach(host); game.Attach(viewer);
        game.Handle(host, new(null!));
        check(host.Events[^1].Type == "error", "malformed command names are rejected before dispatch or rate-budget lookup");
        check(CustomWordLists.Parse("Apple, apple, Pear, , Peach").SequenceEqual(new[] { "Apple", "Pear", "Peach" }),
            "room lists trim and deduplicate entries without counting empty separators");
        foreach (string bad in new[] { "x", "a\u001bb", new string('a', 49), new string('a', 1601), string.Join(',', Enumerable.Range(0, 65).Select(i => "Word " + i)) })
        {
            bool rejected = false; try { CustomWordLists.Parse(bad); } catch (ArgumentException) { rejected = true; }
            check(rejected, "malformed or oversized room word lists are rejected");
        }
        const string custom = "Private apple, Private pear, Private peach, Private melon, Private berry, Private plum";
        game.Handle(host, new("create", Options: new("Custom list check", Rounds: 1, WordLists: [], WordChoiceCount: 6, CustomWordCount: 0), CustomWords: custom));
        check(host.State.Options.CustomWordCount == 6 && host.Events.Last(e => e.Room is not null).HostWords?.Length == 6,
            "server replaces forged custom-list metadata and returns editable words only to the host");
        game.Handle(viewer, new("join", Text: host.State.Code));
        check(viewer.Events.All(e => e.HostWords is null) && !Encoding.UTF8.GetString(Protocol.Encode(viewer.Events)).Contains("Private apple"),
            "joining peers receive custom-list counts but no private room word bank");
        game.Handle(viewer, new("configure", Options: host.State.Options, CustomWords: "Injected word"));
        check(viewer.Events[^1].Type == "error" && host.State.Options.CustomWordCount == 6, "a non-host cannot replace room words");
        game.Handle(host, new("start"));
        check(host.State.WordChoices?.Length == 6 && host.Events.Last(e => e.Room is not null).HostWords is null,
            "custom-only rooms privately offer the configured number without broadcasting a bank");
        game.Handle(host, new("configure", Options: host.State.Options, CustomWords: "Injected word"));
        check(host.Events[^1].Type == "error", "even the host cannot change word pools during a match");
        game.Handle(host, new("choose-word", Text: host.State.WordChoices![0], TurnId: host.State.TurnId));
        game.Handle(viewer, new("like-drawing", TurnId: host.State.TurnId));
        check(host.State.DrawingLikes == 1 && accounts.Leaderboard(a.AccountId, "likes", 0).Entries.All(e => e.Likes == 0),
            "custom-list drawing likes remain in-room and cannot farm lifetime rankings");
        game.Handle(viewer, new("chat", Text: host.State.Word));
        clock.Advance(7); game.Tick();
        game.Handle(viewer, new("choose-word", Text: viewer.State.WordChoices![0], TurnId: viewer.State.TurnId));
        game.Handle(host, new("chat", Text: viewer.State.Word));
        clock.Advance(7); game.Tick();
        check(host.State.Phase == "results" && host.State.MatchResults!.All(r => r.Points == 1100) && accounts.Statistics(a.AccountId)?.Games == 0,
            "completed custom-list games preserve normal room awards without adding lifetime scores or games");
        game.Handle(host, new("suggest", Kind: "word", Category: "FFXIV", Text: "Suggested flower"));
        check(host.Events[^1].Type == "suggestion-sent" && accounts.ReadSuggestions() is [{ Kind: "word", Category: "FFXIV" }],
            "authenticated word submissions enter only the private operator inbox");
        game.Handle(host, new("suggest", Kind: "suggestion", Text: "A second suggestion"));
        check(host.Events[^1].Type == "error" && accounts.ReadSuggestions().Length == 1, "suggestion cooldown is authoritative and persists in SQLite");
        for (int i = 0; i < 4; i++) { clock.Advance(61); game.Handle(host, new("suggest", Kind: "suggestion", Text: "A useful improvement " + i)); }
        clock.Advance(61); game.Handle(host, new("suggest", Kind: "suggestion", Text: "Quota overflow check"));
        check(host.Events[^1].Type == "error" && accounts.ReadSuggestions().Length == 5, "the five-per-day suggestion quota cannot be bypassed with new sessions");
        game.Handle(viewer, new("suggest", Kind: "word", Category: "Unknown category", Text: "Another flower"));
        check(viewer.Events[^1].Type == "suggestion-sent" && accounts.ReadSuggestions().Any(s => s.Category == "Unknown category"),
            "word suggestions can propose a new category without changing the live word catalog");
        foreach (string? category in new string?[] { null, "x", new string('a', 41), "Bad\ncategory" })
        {
            game.Handle(viewer, new("suggest", Kind: "word", Category: category, Text: "Another flower"));
            check(viewer.Events[^1].Type == "error", "proposed categories still enforce bounded non-control names");
        }
        game.Handle(viewer, new("suggest", Kind: "suggestion", Text: "Bad terminal \u001b[31m escape"));
        check(viewer.Events[^1].Type == "error", "terminal escape/control characters are rejected from stored submissions");
        using var reopened = new AccountStore(Path.Combine(working, "release-polish"));
        check(reopened.ReadSuggestions().Length == 6 && reopened.ReadSuggestions()[0].Number > reopened.ReadSuggestions()[^1].Number,
            "the private suggestion inbox survives restart and reads newest first");
        var limits = new InputRateLimiter(clock);
        check(Enumerable.Range(0, 3).All(_ => limits.Allow("sync")) && !limits.Allow("sync"), "canvas snapshot bursts have a dedicated bounded budget");
        check(Enumerable.Range(0, 4).All(_ => limits.Allow("leaderboard")) && !limits.Allow("leaderboard"), "leaderboard queries have a dedicated bounded budget");
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    private sealed class Peer(Guid id) : IGamePeer
    {
        public Guid Id { get; } = id;
        public string? RoomCode { get; set; }
        public List<ServerEvent> Events { get; } = [];
        public RoomView State => Events.Last(e => e.Room is not null).Room!;
        public void Send(ServerEvent message) => Events.Add(message);
        public bool Allow(string type) => true;
        public void Close() { }
    }
}
