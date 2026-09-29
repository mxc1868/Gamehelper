using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using BloodyBot.Combat;
using Bloodybot2.Configuration;
using Bloodybot2.Runtime;
using Bloodybot2.Web;

var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); checks++; }
void Reject(Action action, string name)
{
    try { action(); } catch (Exception ex) when (ex is ArgumentException or JsonException) { Check(true, name); return; }
    throw new Exception("FAIL accepted " + name);
}
BotConfig Config(bool preview = true) => new() { Preview = preview, Combat = new() { Enabled = true, Rules = [new() { Name = "精英技能", Key = 'Q', PauseMilliseconds = 0, CooldownMilliseconds = 1000 }] } };
Observation Frame(long now) => new(new(now, true, new(Vector2.Zero, true, 75, 50, 90,
    new HashSet<string> { "test_buff" }, new HashSet<string> { "test_skill" }),
    [new(12, EnemyRarity.Rare, new(20, 0), true, true, true), new(13, EnemyRarity.Unique, new(25, 0), true, true, true)]),
    "测试角色", "测试区域", ["测试角色", "玩家二"], ["test_skill", "other_skill"]);

var root = Path.Combine(Path.GetTempPath(), "Bloodybot2-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var config = Config();
    Check(BotConfig.Parse(config.Serialize()).Combat.Rules[0].Key == 'Q', "round trip preserves combat fields");
    Check(new BotConfig().Preview && new BotConfig().Combat.Rules.Count == 0, "first run is empty and preview only");
    Reject(() => BotConfig.Parse("null"), "null document");
    Reject(() => BotConfig.Parse("{\"schemaVersion\":2}"), "unsupported schema");
    Reject(() => BotConfig.Parse("{\"simulacrum\":{}}"), "unknown configuration rejected");
    foreach (var invalid in new Action<BotConfig>[] {
        c => c.Name = null!, c => c.Combat = null!, c => c.Combat.Rules = null!, c => c.Combat.Rules.Add(null!),
        c => c.Combat.Rules[0].Key = 'W', c => c.Combat.Rules[0].Range = float.NaN,
        c => c.Combat.Rules[0].Range = 151, c => c.Combat.Rules[0].Rarities = EnemyRarity.None,
        c => c.Combat.Rules[0].Rarities = (EnemyRarity)32, c => c.Combat.Rules[0].MinimumEnemies = 0,
        c => c.Combat.Rules[0].CooldownMilliseconds = 299, c => c.Combat.Rules[0].PressMilliseconds = 201,
        c => c.Combat.Rules[0].PauseMilliseconds = 2001, c => c.Combat.Rules[0].ReadySkill = null!,
        c => c.Combat.Rules.Add(c.Combat.Rules[0]), c => c.Combat.Rules = Enumerable.Range(0,33).Select(_ => new CombatRule()).ToList()
    }) { var bad = Config(); invalid(bad); Reject(bad.Validate, "reject invalid config " + checks); }

    var store = new ConfigStore(Path.Combine(root, "store"));
    store.Load(); Check(!File.Exists(store.FilePath), "loading defaults does not write");
    store.Save(config); config.Name = "第二版"; store.Save(config);
    Check(BotConfig.Parse(File.ReadAllText(store.FilePath + ".bak")).Name == "我的配置", "atomic save retains previous valid version");
    using (var held = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        var failed = false;
        try { store.Save(Config()); } catch (IOException) { failed = true; }
        Check(failed, "locked file save fails");
    }
    Check(store.Load().Name == "第二版", "failed save preserves previous document");
    Check(!Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp").Any(), "failed save cleans temporary file");
    File.WriteAllText(store.FilePath, "broken");
    Check(store.Load().Name == "我的配置" && store.Warning.Length > 0, "corrupt primary recovers backup with warning");
    Check(File.ReadAllText(store.FilePath) == "broken", "recovery never overwrites damaged primary");
    store.Save(Config());
    Check(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.invalid-*").Length == 1, "explicit save archives damaged primary");
    Check(BotConfig.Parse(File.ReadAllText(store.FilePath + ".bak")).Name == "我的配置", "recovery save preserves valid backup");
    File.Delete(store.FilePath);
    Check(store.Load().Combat.Rules.Count == 1 && store.Warning.Length > 0, "missing primary recovers backup");

    var input = new FakeInput();
    var navigation = new FakeNavigation();
    var runtime = new BotRuntime(input, navigation);
    runtime.Apply(Config(), 1); runtime.Start(1000); runtime.Tick(Frame(1000), "", 1, 1000, () => true);
    Check(runtime.Status().Action?.Key == 'Q' && input.Presses == 0, "preview evaluates without sending input");
    runtime.Apply(Config(false), 2); Check(!runtime.Status().Running, "save stops run");
    runtime.Start(1010); runtime.Tick(Frame(1010), "", 1, 1010, () => true);
    Check(input.Presses == 0, "stale scan revision cannot use new config");
    navigation.Allow = false; runtime.Tick(Frame(1020), "", 2, 1020, () => true);
    Check(input.Presses == 0, "navigation can reserve the tick");
    navigation.Allow = true; runtime.Tick(Frame(1030), "chat", 2, 1030, () => true);
    Check(input.Presses == 0, "UI blocked tick does not send");
    runtime.Tick(Frame(1040), "", 2, 1040, () => false);
    Check(input.Presses == 0, "final host recheck prevents send");
    input.Accept = false; runtime.Tick(Frame(1050), "", 2, 1050, () => true);
    Check(runtime.Status().AcceptedInputs == 0, "rejected input does not consume cooldown");
    input.Accept = true; runtime.Tick(Frame(1060), "", 2, 1060, () => true);
    Check(input.Presses == 1 && runtime.Status().AcceptedInputs == 1, "accepted input recorded");
    runtime.Stop(); runtime.Start(1070); runtime.Tick(Frame(1070), "", 2, 1070, () => true);
    Check(input.Presses == 1, "stop start preserves cooldown");
    runtime.Tick(Frame(2060), "", 2, 2060, () => true);
    Check(input.Presses == 2, "cooldown expires");
    runtime.Watchdog(2070, false, false); Check(!input.Held, "focus loss releases input");
    runtime.Watchdog(2500, true, false); Check(!runtime.Status().Running, "F9 / stalled frames stop run");
    runtime.Start(2600); runtime.Watchdog(2601, true, true); Check(!runtime.Status().Running, "escape stops even without game tick");
    runtime.Start(2700); runtime.Tick(Frame(2000), "", 2, 2700, () => true);
    Check(input.Presses == 2, "stale combat observation rejected");
    var read = runtime.ReadConfig(); read.Config.Combat.Rules.Clear();
    Check(runtime.ReadConfig().Config.Combat.Rules.Count == 1, "config reads cannot mutate runtime");
    var heldInput = new FakeInput { BlockedKey = 'Q' };
    var heldRuntime = new BotRuntime(heldInput);
    var heldConfig = Config();
    heldConfig.Combat.Rules.Add(new() { Name = "Secondary", Key = 'E' });
    heldRuntime.Apply(heldConfig, 1); heldRuntime.Start(3000); heldRuntime.Tick(Frame(3000), "", 1, 3000, () => true);
    Check(heldRuntime.Status().Action?.Key == 'E', "manually held skill does not starve later rules");

    var webStore = new ConfigStore(Path.Combine(root, "web"));
    var webRuntime = new BotRuntime(new FakeInput());
    using var server = new WebConfigServer(webStore, webRuntime);
    var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
    server.Start(port);
    using (var secondServer = new WebConfigServer(new ConfigStore(Path.Combine(root, "second-web")), new BotRuntime(new FakeInput())))
    {
        secondServer.Start(new Uri(server.Url).Port);
        Check(secondServer.Url != server.Url, "occupied loopback port advances to a fresh listener");
        using var secondClient = new HttpClient();
        Check((await secondClient.GetAsync(secondServer.Url)).IsSuccessStatusCode, "fallback port serves the frontend");
    }
    using var client = new HttpClient { BaseAddress = new Uri(server.Url), Timeout = TimeSpan.FromSeconds(8) };
    var boot = JsonDocument.Parse(await client.GetStringAsync("api/bootstrap")).RootElement;
    var token = boot.GetProperty("token").GetString()!;
    async Task<HttpResponseMessage> Send(string path, string method, object body, string? auth = null, string? origin = null, string? host = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path)
        { Content = new StringContent(JsonSerializer.Serialize(body, BotConfig.Json), Encoding.UTF8, "application/json") };
        if (auth != null) request.Headers.Add("X-Bloodybot2-Token", auth);
        if (origin != null) request.Headers.Add("Origin", origin);
        if (host != null) request.Headers.Host = host;
        return await client.SendAsync(request);
    }
    Check((await client.GetStringAsync("/")).Contains("战斗编排"), "embedded Chinese frontend served");
    Check((await client.GetAsync("styles.css")).Headers.Contains("Content-Security-Policy"), "web responses carry CSP");
    Check((await Send("api/config", "PUT", new { revision = 1, config = Config() })).StatusCode == HttpStatusCode.Forbidden, "save requires session token");
    Check((await Send("api/run", "POST", new { running = true }, token, "https://example.com")).StatusCode == HttpStatusCode.Forbidden, "foreign origin blocked");
    Check((await Send("api/run", "POST", new { running = true }, token, host: "evil.invalid")).StatusCode != HttpStatusCode.OK, "foreign Host blocked");
    Check((await Send("api/config", "PUT", new { revision = 1, config = Config() }, token)).StatusCode == HttpStatusCode.OK, "valid config saved");
    Check(webRuntime.ReadConfig().Revision == 2 && File.Exists(webStore.FilePath), "save publishes runtime only after disk");
    Check((await Send("api/config", "PUT", new { revision = 1, config = Config() }, token)).StatusCode == HttpStatusCode.Conflict, "stale browser revision rejected");
    Check((await Send("api/config", "PUT", new { revision = 2, config = new { schemaVersion = 99 } }, token)).StatusCode == HttpStatusCode.UnprocessableEntity, "invalid config is 422");
    using (var held = new FileStream(webStore.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        Check((await Send("api/config", "PUT", new { revision = 2, config = Config() }, token)).StatusCode == HttpStatusCode.InternalServerError, "disk failure reported to browser");
    Check(webRuntime.ReadConfig().Revision == 2, "disk failure does not publish or increment revision");
    Check((await Send("api/run", "POST", new { running = true }, token)).StatusCode == HttpStatusCode.OK && webRuntime.Status().Running, "web start reaches runtime");
    Check((await Send("api/run", "POST", new { running = false }, token)).StatusCode == HttpStatusCode.OK && !webRuntime.Status().Running, "web stop works without DrawUI");
    Check((await Send("api/config", "PUT", new { padding = new string('x', 270000) }, token)).StatusCode == HttpStatusCode.RequestEntityTooLarge, "oversized config rejected");
    Check(!(await client.GetAsync("api/bootstrap")).Headers.Contains("Access-Control-Allow-Origin"), "no cross-origin grants");
    Console.WriteLine($"All {checks} Bloodybot2 checks passed. Fake input only; no game memory or Win32 calls.");

    if (args.Contains("--serve"))
    {
        Console.WriteLine("UI_TEST_URL=" + server.Url);
        using var tick = new Timer(_ =>
        {
            var now = Environment.TickCount64;
            webRuntime.Tick(Frame(now), "", webRuntime.ReadConfig().Revision, now, () => true);
        }, null, 0, 75);
        Console.WriteLine("Press Enter to stop the isolated fake server.");
        await Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(15)), Console.In.ReadLineAsync());
    }
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
finally { Directory.Delete(root, recursive: true); }

sealed class FakeInput : IBotInput
{
    public bool Accept = true;
    public int Presses;
    public bool Held;
    public int BlockedKey;
    public bool IsKeyAvailable(int key) => key != this.BlockedKey;
    public bool TryPress(int key, int milliseconds, long now) { if (!this.Accept) return false; this.Presses++; this.Held = true; return true; }
    public void Release() => this.Held = false;
    public void Expire(long now, bool allowed) { if (!allowed) this.Release(); }
}
sealed class FakeNavigation : INavigationMode
{
    public bool Allow = true;
    public string Name => "Fake";
    public bool AllowsCombat(CombatSnapshot snapshot, long now) => this.Allow;
    public void Stop() { }
}
