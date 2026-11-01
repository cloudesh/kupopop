using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Kupopop;

int passed = 0;
string root = Path.GetFullPath(args.FirstOrDefault() ?? Environment.CurrentDirectory);
string working = Path.Combine(root, ".build", "test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(working);

void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    passed++; Console.WriteLine("PASS " + description);
}

var canvas = new PixelCanvas(64, 64);
canvas.Apply(new("pencil", 10, 10, 50, 10, 1, 1));
canvas.Apply(new("pencil", 50, 10, 50, 50, 1, 1));
canvas.Apply(new("pencil", 50, 50, 10, 50, 1, 1));
canvas.Apply(new("pencil", 10, 50, 10, 10, 1, 1));
canvas.Apply(new("fill", 20, 20, 20, 20, 4));
Check(canvas.Pixels[20 * 64 + 20] == 4 && canvas.Pixels[0] == 0, "fill remains inside a closed boundary");
canvas.Apply(new("eraser", 20, 20, 22, 22, 1, 3));
Check(canvas.Pixels[20 * 64 + 20] == 0, "eraser restores canvas white");
Check(!canvas.Apply(new("fill", -1, 0, 0, 0)) && !canvas.Apply(new("pencil", 0, 0, 0, 0, 99)), "invalid drawing coordinates and palette entries rejected");
Check(AvatarGenerator.Generate(123).SequenceEqual(AvatarGenerator.Generate(123)) &&
    !AvatarGenerator.Generate(123).SequenceEqual(AvatarGenerator.Generate(456)), "default avatars are stable per seed and vary across seeds");
var generatedFaces = Enumerable.Range(0, 128).Select(AvatarGenerator.Generate).ToArray();
Check(generatedFaces.All(face => face.Length == Protocol.AvatarSize * Protocol.AvatarSize && face.All(color => color < Palette.Colors.Length)) &&
    generatedFaces.Select(Convert.ToHexString).Distinct().Count() > 80, "moogle-like defaults remain valid editable avatars with varied faces and pom colors");
var portraits = Enumerable.Range(0, 128).Select(AvatarGenerator.Rgba).ToArray();
Check(portraits.All(p => p[3] == 0 && p[(63 * 64 + 63) * 4 + 3] == 0 &&
    p[(34 * 64 + 32) * 4 + 3] == 255 && p[(34 * 64 + 32) * 4] == 255),
    "default portrait exterior blends into paper while the white moogle face stays opaque across seeds");
canvas.Load(new byte[64 * 64]);
byte[] customPortrait = canvas.Rgba();
Check(Enumerable.Range(0, 64 * 64).All(i => customPortrait[i * 4 + 3] == 255),
    "editable/custom portrait conversion retains every background pixel and its opacity");
var retrySchedule = new ConnectionRetry();
bool delaysBounded = retrySchedule.Due(0);
long retryTime = 0;
for (int i = 0; i < 100; i++)
{
    retrySchedule.Failed(retryTime);
    delaysBounded &= !retrySchedule.Due(retrySchedule.NextAttempt - 1) && retrySchedule.Due(retrySchedule.NextAttempt) &&
        retrySchedule.NextAttempt - retryTime is >= 2000 and <= 30000;
    retryTime = retrySchedule.NextAttempt;
}
Check(delaysBounded, "automatic reconnection continues beyond eight failures without hammering the service");
retrySchedule.Connected();
retrySchedule.Failed(100);
Check(retrySchedule.Attempts == 1 && retrySchedule.NextAttempt == 2100, "successful sign-in resets retry backoff for the next outage");
var pom = new PomMotion();
pom.Step(100, 1f / 60, true);
Check(pom.Angle == 0, "decorative pom stays still until the window moves");
pom.Step(160, 1f / 60, true);
Check(pom.Angle < 0 && MathF.Abs(pom.Angle) <= 0.62f, "window movement gives the pom a bounded opposing swing");
bool boundedPom = true;
for (int i = 0; i < 1200; i++)
{
    pom.Step(i % 2 == 0 ? -10000 : 10000, i % 3 == 0 ? 0.1f : 1f / 144, true);
    boundedPom &= float.IsFinite(pom.Angle) && MathF.Abs(pom.Angle) <= 0.62f;
}
Check(boundedPom, "pom remains finite and bounded during abrupt movement and uneven frame times");
for (int i = 0; i < 600; i++) pom.Step(10000, 1f / 60, true);
Check(pom.Angle == 0, "pom settles fully after movement stops");
pom.Step(100, 1f / 60, true); pom.Step(100, 1f / 60, false);
bool reducedPom = pom.Angle == 0;
pom.Step(200, 1f / 60, true); pom.Step(200, 1, true);
Check(reducedPom && pom.Angle == 0, "reduced motion and long frame pauses reset decorative movement");
if (OperatingSystem.IsWindows())
{
    var vault = new CredentialVault(Path.Combine(working, "vault"));
    var origin = new Uri("https://localhost:7443/");
    var localAccount = new DeviceAccount(Guid.NewGuid(), "a-fake-test-token-not-a-real-credential");
    vault.Save(origin, localAccount);
    Check(vault.Load(origin) == localAccount, "Windows DPAPI device credential round-trips for the current user");
    string protectedFile = Directory.GetFiles(Path.Combine(working, "vault")).Single();
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(protectedFile)).Contains(localAccount.AccessToken) &&
        vault.Load(new Uri("https://other.example/")) is null, "device credentials are encrypted and separated by server origin");
}
foreach (string bad in new[] { "http://example.com", "https://127.0.0.1", "https://user:secret@example.com", "https://example.com/path", "https://example.com/?secret=value" })
{
    bool rejected = false;
    try { using var _ = new RemoteClient(new Uri(bad)); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "unsafe endpoint rejected: " + new Uri(bad).GetLeftPart(UriPartial.Path).Replace("user:secret@", ""));
}
Check(await RejectHealthResponse("{\"protocol\":999}"), "connection gate rejects an incompatible service protocol");
Check(await RejectHealthResponse("{\"service\":\"not the game\"}"), "connection gate rejects a response without the game protocol");

var accounts = new AccountStore(Path.Combine(working, "accounts"));
FeatureChecks.Run(working, Check);
RoundFlowChecks.Run(working, Check);
RoomRulesChecks.Run(working, Check);
var credentials = Enumerable.Range(0, 18).Select(i => accounts.Create("Artist " + i)).ToArray();
string accountFile = File.ReadAllText(Path.Combine(working, "accounts", "accounts.json"));
Check(!accountFile.Contains(credentials[0].AccessToken) && !accountFile.Contains(credentials[0].RecoveryCode), "account persistence contains credential verifiers, not bearer secrets");
Check(new AccountStore(Path.Combine(working, "accounts")).Authenticate(credentials[0].AccessToken)?.Id == credentials[0].AccountId, "accounts survive server restart");
var recovered = accounts.Recover(credentials[17].RecoveryCode)!;
Check(recovered.AccountId == credentials[17].AccountId && accounts.Authenticate(credentials[17].AccessToken) is null &&
    accounts.Recover(credentials[17].RecoveryCode) is null, "recovery preserves identity and invalidates old access/recovery credentials");

var time = new TestClock();
var engine = new GameEngine(accounts, NullLogger<GameEngine>.Instance, time);
var peers = credentials.Take(17).Select(c => new TestPeer(c.AccountId)).ToArray();
foreach (var peer in peers) engine.Attach(peer);
engine.Handle(peers[0], new("create", Options: new("Private room")));
string code = peers[0].State.Code;
engine.Handle(peers[16], new("list"));
Check(peers[16].Events.Last(e => e.Type == "rooms").Rooms!.Length == 0, "private rooms do not appear in public discovery");
for (int i = 1; i < 16; i++) engine.Handle(peers[i], new("join", Text: code));
Check(peers[0].State.Players.Length == 16, "a room supports sixteen players including the drawer");
engine.Handle(peers[16], new("join", Text: code));
Check(peers[16].Events.Last().Type == "error", "seventeenth player rejected");
engine.Handle(peers[1], new("start"));
Check(peers[1].Events.Last().Type == "error", "non-host cannot start a match");
engine.Handle(peers[0], new("start"));
engine.Handle(peers[0], new("choose-word", Text: peers[0].State.WordChoices![0], TurnId: peers[0].State.TurnId));
string word = peers[0].State.Word!;
Check(!string.IsNullOrEmpty(word) && peers.Skip(1).Take(15).All(p => p.State.Word is null), "active answer is delivered to the drawer, not guessers");
engine.Handle(peers[1], new("draw", Draw: new("pencil", 0, 0, 4, 4), TurnId: peers[0].State.TurnId));
Check(peers[1].Events.Last().Type == "error", "non-drawer cannot modify the canvas");
engine.Handle(peers[0], new("draw", Draw: new("pencil", 0, 0, 4, 4), TurnId: Guid.NewGuid()));
Check(peers[0].Events.Last().Type == "error", "stale turn IDs cannot modify a new turn");
engine.Handle(peers[0], new("draw", Draw: new("fill", 0, 0, 0, 0, 8), TurnId: peers[0].State.TurnId));
engine.Handle(peers[1], new("sync"));
Check(peers[1].State.Canvas!.All(p => p == 8), "server canvas snapshot includes authoritative fill operations");
engine.Handle(peers[1], new("chat", Text: "  " + word.ToUpperInvariant() + "!  "));
Check(peers[1].State.Players.Single(p => p.Profile.Id == peers[1].Id).Score == 1000 &&
    peers[2].State.Word is null, "first solver earns 1000 and answer stays hidden from remaining guessers");
Check(peers[2].Events.Where(e => e.Chat is not null).All(e => !e.Chat!.Text.Contains(word, StringComparison.OrdinalIgnoreCase)), "successful guess is not broadcast as answer text");
engine.Handle(peers[2], new("chat", Text: word));
Check(peers[2].State.Players.Single(p => p.Profile.Id == peers[2].Id).Score == 940 &&
    peers[0].State.Players.Single(p => p.Profile.Id == peers[0].Id).Score == 200, "solve-order scoring and drawer reward are server-calculated");
int unsolvedBefore = peers[3].Events.Count;
engine.Handle(peers[1], new("chat", Text: "done-mode-secret"));
engine.Handle(peers[0], new("chat", Text: "drawer-private"));
Check(peers[3].Events.Count == unsolvedBefore && peers[2].Events.Any(e => e.Chat?.Text == "done-mode-secret") &&
    peers[2].Events.Any(e => e.Chat?.Text == "drawer-private"), "done-mode and drawer messages are never delivered to remaining guessers");
engine.Handle(peers[1], new("chat", Text: word));
Check(peers[1].State.Players.Single(p => p.Profile.Id == peers[1].Id).Score == 1000, "repeated correct guesses cannot award duplicate points");
time.Advance(31); engine.Tick();
Check(peers[3].State.Hint.Any(char.IsLetter), "server reveals hints on the configured schedule");
var returning = new TestPeer(peers[2].Id);
engine.Detach(peers[2]); engine.Attach(returning);
Check(returning.State.YouAreDone && returning.State.Canvas is not null &&
    returning.State.Players.Single(p => p.Profile.Id == returning.Id).Score == 940, "reconnection restores solved status, score, and canvas");
engine.Revoke(returning.Id);
int revokedEvents = returning.Events.Count;
engine.Handle(returning, new("chat", Text: "revoked"));
Check(returning.Closed && returning.Events.Count == revokedEvents, "revocation immediately prevents further commands from the old connection");

var matchTime = new TestClock();
var match = new GameEngine(accounts, NullLogger<GameEngine>.Instance, matchTime);
var a = new TestPeer(credentials[0].AccountId); var b = new TestPeer(credentials[1].AccountId);
match.Attach(a); match.Attach(b);
match.Handle(a, new("create", Options: new("Full match", IsPublic: true)));
match.Handle(b, new("join", Text: a.State.Code));
match.Handle(a, new("start"));
match.Handle(a, new("choose-word", Text: a.State.WordChoices![0], TurnId: a.State.TurnId));
var drawers = new List<Guid>();
for (int turn = 0; turn < 4; turn++)
{
    var drawer = a.State.DrawerId == a.Id ? a : b;
    var solver = ReferenceEquals(drawer, a) ? b : a;
    drawers.Add(drawer.Id);
    match.Handle(solver, new("chat", Text: drawer.State.Word));
    Check(a.State.Phase == "intermission", "all solved ends drawing early, turn " + (turn + 1));
    matchTime.Advance(6.1); match.Tick();
    if (a.State.Phase == "choosing")
    {
        var chooser = a.State.DrawerId == a.Id ? a : b;
        match.Handle(chooser, new("choose-word", Text: chooser.State.WordChoices![0], TurnId: chooser.State.TurnId));
    }
}
Check(a.State.Phase == "results" && drawers.Count(d => d == a.Id) == 2 && drawers.Count(d => d == b.Id) == 2,
    "two rounds give every player exactly two drawing turns before results");
match.Handle(b, new("list"));
var listing = b.Events.Last(e => e.Type == "rooms");
string publicJson = Encoding.UTF8.GetString(Protocol.Encode(listing));
Check(listing.Rooms!.Length == 1 && !publicJson.Contains(a.Id.ToString()) && !publicJson.Contains("displayName"), "public listings expose rooms and counts without participant profiles");
match.Handle(a, new("start"));
match.Handle(a, new("choose-word", Text: a.State.WordChoices![0], TurnId: a.State.TurnId));
matchTime.Advance(91);
var expiredSolver = a.State.DrawerId == a.Id ? b : a;
var expiredDrawer = ReferenceEquals(expiredSolver, a) ? b : a;
match.Handle(expiredSolver, new("chat", Text: expiredDrawer.State.Word));
Check(a.State.Phase == "intermission" && a.State.Players.All(p => p.Score == 0), "late guesses cannot score after the server deadline");

// Real HTTP/WebSocket integration, with HTTP restricted to this isolated loopback test.
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
string serverDll = Path.Combine(root, "server", "Kupopop.Server", "bin", "Release", "net10.0", "Kupopop.Server.dll");
var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
start.ArgumentList.Add(serverDll); start.ArgumentList.Add("--test-http");
string liveDirectory = Path.Combine(working, "live");
var loadStore = new AccountStore(Path.Combine(liveDirectory, "data"));
var loadAccounts = Enumerable.Range(0, 16).Select(i => loadStore.Create("Network Artist " + i)).ToArray();
start.Environment["KUPOPOP_ROOT"] = liveDirectory;
start.Environment["KUPOPOP_PORT"] = port.ToString();
start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
using var process = Process.Start(start)!;
var output = new StringBuilder();
process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
process.BeginOutputReadLine(); process.BeginErrorReadLine();
try
{
    using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
    bool ready = false;
    for (int i = 0; i < 100 && !process.HasExited; i++)
    {
        try { ready = (await http.GetAsync("/health")).IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    if (!ready) throw new Exception("Integration server did not start: " + output);
    using var first = new RemoteClient(http.BaseAddress!, allowTestHttp: true);
    await first.CheckHealthAsync();
    Check(true, "automatic connection validates the live service protocol before account setup or sign-in");
    using var second = new RemoteClient(http.BaseAddress!, allowTestHttp: true);
    using var third = new RemoteClient(http.BaseAddress!, allowTestHttp: true);
    var one = await first.CreateAsync("Live One");
    var two = await second.CreateAsync("Live Two");
    var three = await third.CreateAsync("Live Three");
    await first.ConnectAsync(one.AccessToken); await second.ConnectAsync(two.AccessToken); await third.ConnectAsync(three.AccessToken);
    first.Send(new("create", Options: new("Live private")));
    var firstState = await WaitEvent(first, e => e.Room is not null);
    second.Send(new("join", Text: firstState.Room!.Code));
    third.Send(new("join", Text: firstState.Room.Code));
    await WaitEvent(first, e => e.Room?.Players.Length == 3);
    first.Send(new("start"));
    var drawing = await WaitEvent(first, e => e.Room?.Phase == "drawing");
    await WaitEvent(second, e => e.Room?.Phase == "drawing");
    await WaitEvent(third, e => e.Room?.Phase == "drawing");
    first.Send(new("draw", Draw: new("pencil", 5, 5, 30, 30, 4), TurnId: drawing.Room!.TurnId));
    var drawReceived = await WaitEvent(second, e => e.Type == "draw");
    Check(drawReceived.Draw?.Color == 4, "real WebSocket clients receive synchronized drawing");
    first.Send(new("undo", TurnId: drawing.Room.TurnId));
    Check((await WaitEvent(second, e => e.Type == "canvas")).Canvas!.All(c => c == 0) &&
        (await WaitEvent(first, e => e.Type == "canvas")).Canvas!.All(c => c == 0), "real WebSocket undo synchronizes drawer and spectator snapshots");
    second.Send(new("chat", Text: drawing.Room.Word));
    await WaitEvent(second, e => e.Room?.YouAreDone == true);
    second.Send(new("chat", Text: "integration-private-message"));
    Check((await WaitEvent(first, e => e.Chat?.Text == "integration-private-message")).Chat!.Done,
        "real network done-mode message reaches drawer");
    await Task.Delay(100);
    var thirdEvents = new List<ServerEvent>();
    while (third.TryRead(out var evt)) if (evt is not null) thirdEvents.Add(evt);
    Check(thirdEvents.All(e => e.Chat?.Text != "integration-private-message" && e.Room?.Word is null),
        "real network unsolved client receives neither done-mode content nor answer");
    third.Send(new("vote-kick", TurnId: drawing.Room.TurnId));
    Check((await WaitEvent(third, e => e.Room?.KickVote?.Votes == 1)).Room!.KickVote!.Required == 2,
        "real network ballot sends only tally, threshold and per-client voting status");
    second.Send(new("vote-kick", TurnId: drawing.Room.TurnId));
    Check((await WaitEvent(first, e => e.Type == "left")).Error == "The room voted to remove you.",
        "real network unanimous vote removes the drawer and notifies their client");
    using var wrongVersion = new ClientWebSocket();
    wrongVersion.Options.SetRequestHeader("Authorization", "Bearer " + one.AccessToken);
    wrongVersion.Options.SetRequestHeader("X-Kupopop-Protocol", "999");
    bool mismatch = false;
    try { await wrongVersion.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None); }
    catch (WebSocketException) { mismatch = true; }
    Check(mismatch, "server rejects incompatible protocol handshakes");
    using var anonymous = new ClientWebSocket();
    anonymous.Options.SetRequestHeader("X-Kupopop-Protocol", "1");
    bool unauthorized = false;
    try { await anonymous.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None); }
    catch (WebSocketException) { unauthorized = true; }
    Check(unauthorized, "server rejects unauthenticated WebSocket connections");
    var oversized = await http.PostAsync("/api/accounts", new StringContent("{\"displayName\":\"" + new string('a', 15000) + "\"}", Encoding.UTF8, "application/json"));
    Check(!oversized.IsSuccessStatusCode, "HTTP request size bound rejects oversized registration");
    await first.DisconnectAsync(); await second.DisconnectAsync(); await third.DisconnectAsync();
    var loadClients = loadAccounts.Select(_ => new RemoteClient(http.BaseAddress!, allowTestHttp: true)).ToArray();
    try
    {
        for (int i = 0; i < 16; i++) await loadClients[i].ConnectAsync(loadAccounts[i].AccessToken);
        loadClients[0].Send(new("create", Options: new("Sixteen-client drawing test")));
        var loadRoom = await WaitEvent(loadClients[0], e => e.Room is not null);
        for (int i = 1; i < 16; i++) loadClients[i].Send(new("join", Text: loadRoom.Room!.Code));
        await WaitEvent(loadClients[0], e => e.Room?.Players.Length == 16);
        loadClients[0].Send(new("start"));
        var loadTurn = await WaitEvent(loadClients[0], e => e.Room?.Phase == "drawing");
        for (int i = 0; i < 50; i++)
            loadClients[0].Send(new("draw", Draw: new("pencil", i, 10, i + 1, 10, 5), TurnId: loadTurn.Room!.TurnId));
        for (int i = 1; i < 16; i++)
            await WaitEvent(loadClients[i], e => e.Draw?.X2 == 50);
        Check(true, "sixteen real WebSocket clients join and all fifteen viewers receive a 50-operation drawing burst");
    }
    finally { foreach (var c in loadClients) { await c.DisconnectAsync(); c.Dispose(); } }
    string logText = string.Join('\n', Directory.GetFiles(Path.Combine(liveDirectory, "logs")).Select(ReadShared));
    Check(!logText.Contains(one.AccessToken) && !logText.Contains(one.RecoveryCode) &&
        !logText.Contains("integration-private-message") && !logText.Contains(drawing.Room.Word!),
        "server file diagnostics exclude credentials, private chat, and answers");
    Check((await http.PostAsJsonAsync("/api/accounts", new AccountRequest("Cache Test"))).Headers.CacheControl?.NoStore == true,
        "account credential responses cannot be cached");
}
finally
{
    if (!process.HasExited) process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
}

// TLS smoke test of the published Windows executable; do not modify OS trust.
string packagedExe = Path.Combine(root, "artifacts", "server-win-x64", "Kupopop.Server.exe");
if (OperatingSystem.IsWindows() && File.Exists(packagedExe))
{
    using var tlsListener = new TcpListener(IPAddress.Loopback, 0);
    tlsListener.Start(); int tlsPort = ((IPEndPoint)tlsListener.LocalEndpoint).Port; tlsListener.Stop();
    string tlsRoot = Path.Combine(working, "packaged-tls");
    var tlsStart = new ProcessStartInfo(packagedExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    tlsStart.ArgumentList.Add("--local");
    tlsStart.Environment["KUPOPOP_ROOT"] = tlsRoot;
    tlsStart.Environment["KUPOPOP_PORT"] = tlsPort.ToString();
    using var tlsProcess = Process.Start(tlsStart)!;
    tlsProcess.BeginOutputReadLine(); tlsProcess.BeginErrorReadLine();
    try
    {
        string certPath = Path.Combine(tlsRoot, "data", "localhost.cer");
        for (int i = 0; i < 100 && !File.Exists(certPath) && !tlsProcess.HasExited; i++) await Task.Delay(100);
        if (!File.Exists(certPath)) throw new Exception("Published TLS server did not generate its certificate.");
        using var cert = X509CertificateLoader.LoadCertificateFromFile(certPath);
        using var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, presented, _, errors) =>
        {
            if (presented is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(cert);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(presented);
        };
        using var tlsHttp = new HttpClient(handler);
        bool ready = false;
        for (int i = 0; i < 100 && !tlsProcess.HasExited; i++)
        {
            try { ready = (await tlsHttp.GetAsync($"https://localhost:{tlsPort}/health")).IsSuccessStatusCode; if (ready) break; }
            catch (HttpRequestException) { }
            await Task.Delay(100);
        }
        Check(ready, "self-contained Windows server starts and serves verified localhost TLS without an installed runtime dependency");
        using var ordinary = new HttpClient();
        bool rejected = false;
        try { await ordinary.GetAsync($"https://localhost:{tlsPort}/health"); } catch (HttpRequestException) { rejected = true; }
        Check(rejected, "ordinary clients reject the development certificate until explicitly trusted");
    }
    finally { if (!tlsProcess.HasExited) tlsProcess.Kill(entireProcessTree: true); await tlsProcess.WaitForExitAsync(); }
}
Console.WriteLine($"SUCCESS: {passed} checks passed. Test artifacts: {working}");

static async Task<ServerEvent> WaitEvent(RemoteClient client, Func<ServerEvent, bool> predicate)
{
    var until = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < until)
    {
        while (client.TryRead(out var message))
        {
            if (message?.Type == "error") throw new Exception("Integration command rejected: " + message.Error);
            if (message?.Room is { Phase: "choosing", WordChoices: { Length: > 0 } choices } choosing)
                client.Send(new("choose-word", Text: choices[0], TurnId: choosing.TurnId));
            if (message is not null && predicate(message)) return message;
        }
        await Task.Delay(10);
    }
    throw new TimeoutException("Expected protocol event did not arrive.");
}

static async Task<bool> RejectHealthResponse(string body)
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var serve = Task.Run(async () =>
    {
        using var peer = await listener.AcceptTcpClientAsync();
        using var stream = peer.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (await reader.ReadLineAsync() is { Length: > 0 }) { }
        byte[] response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
        await stream.WriteAsync(response);
    });
    using var client = new RemoteClient(new Uri($"http://localhost:{port}"), allowTestHttp: true);
    try { await client.CheckHealthAsync(); return false; }
    catch (InvalidDataException) { return true; }
    finally { await serve; }
}

static string ReadShared(string path)
{
    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(file);
    return reader.ReadToEnd();
}

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(double seconds) => now = now.AddSeconds(seconds);
}

sealed class TestPeer(Guid id) : IGamePeer
{
    public Guid Id { get; } = id;
    public string? RoomCode { get; set; }
    public bool Closed;
    public List<ServerEvent> Events = [];
    public RoomView State => Events.Last(e => e.Room is not null).Room!;
    public void Send(ServerEvent message) => Events.Add(message);
    public bool Allow(string type) => true;
    public void Close() => Closed = true;
}
