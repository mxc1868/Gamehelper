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
BotConfig Config(bool preview = true) => new() { Mode = "Follow", Follow = new() { LeaderName = "队长" }, Preview = preview, Combat = new() { Enabled = true, Rules = [new() { Name = "精英技能", Key = 'Q', PauseMilliseconds = 0, CooldownMilliseconds = 1000 }] } };
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
    foreach (var version in new[] { 1, 2 })
        foreach (var oldSwitch in new[] { false, true })
        {
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(config.Serialize())!.AsObject();
            legacy["schemaVersion"] = version; legacy["allowControllerWithoutChat"] = oldSwitch;
            var parsed = BotConfig.Parse(legacy.ToJsonString());
            Check(parsed.Combat.Rules[0].Key == 'Q' && !parsed.Serialize().Contains("allowControllerWithoutChat"),
                $"retired controller switch {oldSwitch} accepted and omitted on serialization (v{version})");
        }
    var oldStore = new ConfigStore(Path.Combine(root, "old-controller"));
    Directory.CreateDirectory(Path.GetDirectoryName(oldStore.FilePath)!);
    var oldConfig = System.Text.Json.Nodes.JsonNode.Parse(config.Serialize())!.AsObject();
    oldConfig["allowControllerWithoutChat"] = false;
    var oldText = oldConfig.ToJsonString(); File.WriteAllText(oldStore.FilePath, oldText);
    var loadedOld = oldStore.Load();
    Check(oldStore.Warning.Length == 0 && loadedOld.Combat.Rules[0].Key == 'Q' && File.ReadAllText(oldStore.FilePath) == oldText,
        "old controller setting loads without warnings or rewriting user file");
    oldStore.Save(loadedOld);
    Check(!File.ReadAllText(oldStore.FilePath).Contains("allowControllerWithoutChat") &&
        File.ReadAllText(oldStore.FilePath + ".bak") == oldText, "explicit save retires switch and backs up original config");
    Reject(() => BotConfig.Parse("null"), "null document");
    Reject(() => BotConfig.Parse("{\"schemaVersion\":3}"), "unsupported schema");
    Reject(() => BotConfig.Parse("{\"simulacrum\":{}}"), "unknown configuration rejected");
    Reject(new BotConfig().ValidateStart, "mode selection is required to start");
    var noLeader = Config(); noLeader.Follow.LeaderName = " ";
    Reject(noLeader.ValidateStart, "Follow requires a leader");
    var invalidRoles = Config(); invalidRoles.Follow.LocalCoopFollow = true;
    invalidRoles.Follow.P1Name = " P1 "; invalidRoles.Follow.P2Name = "p1";
    Reject(invalidRoles.ValidateStart, "co-op roles must differ after trimming");
    var v1 = Config();
    var v1Json = System.Text.Json.Nodes.JsonNode.Parse(v1.Serialize())!.AsObject();
    v1Json["schemaVersion"] = 1; v1Json.Remove("mode"); v1Json.Remove("follow"); v1Json.Remove("toggleKey");
    Check(BotConfig.Parse(v1Json.ToJsonString()) is { SchemaVersion: 2, Mode: "", ToggleKey: 0x75 }, "v1 upgrades but requires explicit mode selection");
    var legacyPath = Path.Combine(root, "legacy-settings.txt");
    var legacyJson = """
        {"LeaderName":" Leader ","LocalCoopFollow":true,"P1Name":"P1","P2Name":"P2",
         "ToggleKey":38,"PreviewOnly":false,"CombatUseP2":true,"StopDistance":20,"ResumeDistance":28,"Combat":null}
        """;
    File.WriteAllText(legacyPath, legacyJson);
    var migratedStore = new ConfigStore(Path.Combine(root, "migration"), legacyPath);
    var migrated = migratedStore.Load();
    Check(migrated.Mode == "" && migrated.Follow.LeaderName == "Leader" && migrated.Follow.LocalCoopFollow && migrated.Follow.StopDistance == 20,
        "Follower navigation migrates without choosing a mode");
    Check(!migrated.Preview && migrated.ToggleKey == 0x75 && migrated.MonitorCharacter == "P2",
        "Follower preview / character and invalid hotkey migrate");
    Check(!migrated.Combat.Enabled && migrated.Combat.Rules.Count == 0, "legacy null combat remains disabled");
    Check(!File.Exists(migratedStore.FilePath) && File.ReadAllText(legacyPath) == legacyJson,
        "migration preserves source and never writes defaults");
    Directory.CreateDirectory(Path.GetDirectoryName(migratedStore.FilePath)!);
    File.WriteAllText(migratedStore.FilePath, v1Json.ToJsonString());
    migrated = migratedStore.Load();
    Check(migrated.Preview && migrated.Combat.Rules[0].Key == 'Q' && migrated.Follow.LeaderName == "Leader",
        "existing Bloodybot2 general/combat settings win over Follower");
    File.WriteAllText(legacyPath, legacyJson.Replace(" Leader ", new string('x', 140)));
    migrated = migratedStore.Load();
    Check(migrated.Follow.LeaderName == "" && migrated.Combat.Rules[0].Key == 'Q' && migratedStore.Warning.Contains("无法迁移"),
        "invalid legacy navigation leaves valid Bloodybot2 config intact");
    File.WriteAllText(legacyPath, legacyJson);
    migrated.Mode = "Follow"; migrated.Follow.LeaderName = "New leader"; migratedStore.Save(migrated);
    Check(migratedStore.Load().Follow.LeaderName == "New leader", "saved v2 configuration is never remigrated");
    foreach (var invalid in new Action<BotConfig>[] {
        c => c.Name = null!, c => c.Combat = null!, c => c.Combat.Rules = null!, c => c.Combat.Rules.Add(null!),
        c => c.Combat.Rules[0].Key = 'W', c => c.Combat.Rules[0].Range = float.NaN,
        c => c.Combat.Rules[0].Range = 151, c => c.Combat.Rules[0].Rarities = EnemyRarity.None,
        c => c.Combat.Rules[0].Rarities = (EnemyRarity)32, c => c.Combat.Rules[0].MinimumEnemies = 0,
        c => c.Combat.Rules[0].CooldownMilliseconds = 299, c => c.Combat.Rules[0].PressMilliseconds = 201,
        c => c.Combat.Rules[0].PauseMilliseconds = 2001, c => c.Combat.Rules[0].ReadySkill = null!,
        c => c.Mode = "Simulacrum", c => c.Follow = null!, c => c.ToggleKey = 'W',
        c => c.Follow.ResumeDistance = c.Follow.StopDistance, c => c.Follow.RepathMilliseconds = 1,
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
    var heldRuntime = new BotRuntime(heldInput, new FakeNavigation());
    var heldConfig = Config();
    heldConfig.Combat.Rules.Add(new() { Name = "Secondary", Key = 'E' });
    heldRuntime.Apply(heldConfig, 1); heldRuntime.Start(3000); heldRuntime.Tick(Frame(3000), "", 1, 3000, () => true);
    Check(heldRuntime.Status().Action?.Key == 'E', "manually held skill does not starve later rules");

    var followInput = new FakeInput(); var followNav = new FakeNavigation();
    var followRuntime = new BotRuntime(followInput, followNav);
    var followOnly = Config(false); followOnly.Combat.Enabled = false; followOnly.Combat.Rules.Clear();
    followRuntime.Apply(followOnly, 1); followRuntime.Start(4000);
    followRuntime.Tick(null, "", 1, 4000, () => false);
    Check(followNav.Moves == 1 && followRuntime.Status().Running && followInput.Presses == 0,
        "Follow works without combat rules or combat observations");
    var casting = Config(false); casting.Combat.Rules[0].PauseMilliseconds = 250;
    followRuntime.Apply(casting, 2); followRuntime.Start(5000);
    followRuntime.Tick(Frame(5000), "", 2, 5000, () => true);
    var moves = followNav.Moves;
    followRuntime.Tick(Frame(5100), "", 2, 5100, () => true);
    Check(!followInput.Held && followNav.Moves == moves && followNav.PausedMilliseconds == 330,
        "movement yields for cast pause after skill key is released");
    followRuntime.Tick(Frame(5330), "", 2, 5330, () => true);
    Check(followNav.Moves == moves + 1, "navigation resumes once cast pause ends");
    followNav.EndOnTick = true; followRuntime.Tick(Frame(5340), "", 2, 5340, () => true);
    Check(!followRuntime.Status().Running && !followInput.Held, "terminal navigation failure stops shared runtime");
    var toggleConflict = Config(); toggleConflict.ToggleKey = 'Q';
    heldRuntime.Apply(toggleConflict, 2); heldRuntime.Start(6000); heldRuntime.Tick(Frame(6000), "", 2, 6000, () => true);
    Check(heldRuntime.Status().Action == null, "toggle hotkey is reserved from combat");

    var renderInput = new FakeInput(); var renderNav = new FakeNavigation();
    var renderRuntime = new BotRuntime(renderInput, renderNav);
    renderRuntime.Apply(Config(false), 1);
    renderRuntime.DrawOverlay();
    Check(renderNav.Draws == 1 && renderNav.Ticks == 0 && renderInput.Presses == 0,
        "stopped status can render without advancing navigation or input");
    renderRuntime.Start(7000); renderRuntime.Tick(Frame(7000), "", 1, 7000, () => true);
    for (var frame = 0; frame < 5; frame++) renderRuntime.DrawOverlay();
    Check(renderNav.Draws == 6 && renderNav.Ticks == 1 && renderInput.Presses == 1,
        "all frames between observations render without repeating movement or skills");
    renderRuntime.Tick(Frame(7075), "panel", 1, 7075, () => false);
    renderRuntime.DrawOverlay();
    Check(renderNav.Draws == 7 && renderNav.Ticks == 1 && !renderInput.Held,
        "blocked status still renders while movement and skills remain suspended");
    renderRuntime.Watchdog(7500, true, false);
    renderRuntime.DrawOverlay();
    Check(!renderRuntime.Status().Running && renderNav.Draws == 8 && renderInput.Presses == 1,
        "rendering does not refresh watchdog or restart stopped input");

    var webStore = new ConfigStore(Path.Combine(root, "web"));
    var webRuntime = new BotRuntime(new FakeInput(), new FakeNavigation());
    using var server = new WebConfigServer(webStore, webRuntime);
    var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
    server.Start(port);
    using (var secondServer = new WebConfigServer(new ConfigStore(Path.Combine(root, "second-web")), new BotRuntime(new FakeInput(), new FakeNavigation())))
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
    Check((await client.GetStringAsync("/")).Contains("技能优先级"), "embedded Chinese frontend served");
    Check((await client.GetAsync("styles.css")).Headers.Contains("Content-Security-Policy"), "web responses carry CSP");
    Check((await Send("api/config", "PUT", new { revision = 1, config = Config() })).StatusCode == HttpStatusCode.Forbidden, "save requires session token");
    Check((await Send("api/run", "POST", new { running = true }, token, "https://example.com")).StatusCode == HttpStatusCode.Forbidden, "foreign origin blocked");
    Check((await Send("api/run", "POST", new { running = true }, token, host: "evil.invalid")).StatusCode != HttpStatusCode.OK, "foreign Host blocked");
    Check((await Send("api/run", "POST", new { running = true }, token)).StatusCode == HttpStatusCode.UnprocessableEntity, "HTTP start requires mode selection");
    Check((await Send("api/config", "PUT", new { revision = 1, config = new BotConfig() }, token)).StatusCode == HttpStatusCode.UnprocessableEntity, "HTTP save requires mode selection");
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
    foreach (var oldSwitch in new[] { false, true })
    {
        var legacyRequest = System.Text.Json.Nodes.JsonNode.Parse(Config().Serialize())!.AsObject();
        legacyRequest["allowControllerWithoutChat"] = oldSwitch;
        Check((await Send("api/config", "PUT", new { revision = webRuntime.ReadConfig().Revision, config = legacyRequest }, token)).StatusCode == HttpStatusCode.OK &&
            !File.ReadAllText(webStore.FilePath).Contains("allowControllerWithoutChat"),
            $"old browser request with controller switch {oldSwitch} saves without the retired field");
    }
    var unknownRequest = System.Text.Json.Nodes.JsonNode.Parse(Config().Serialize())!.AsObject();
    unknownRequest["unknownSetting"] = true;
    Check((await Send("api/config", "PUT", new { revision = webRuntime.ReadConfig().Revision, config = unknownRequest }, token)).StatusCode == HttpStatusCode.UnprocessableEntity,
        "retiring one field does not loosen HTTP validation of unknown fields");
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
    public bool SkillHeld => this.Held;
    private long releaseAt;
    public int BlockedKey;
    public bool IsKeyAvailable(int key) => key != this.BlockedKey;
    public bool TryPress(int key, int milliseconds, long now) { if (!this.Accept) return false; this.Presses++; this.Held = true; this.releaseAt = now + milliseconds; return true; }
    public void Release() => this.Held = false;
    public void Expire(long now, bool allowed) { if (!allowed || now >= this.releaseAt) this.Release(); }
}
sealed class FakeNavigation : INavigationMode
{
    public bool Allow = true;
    public bool EndOnTick;
    public int Ticks, Moves, PausedMilliseconds, Draws;
    public string Name => "Follow";
    public string Status => "跟随中";
    public bool IsActive { get; private set; }
    public void Apply(BotConfig config) { this.Stop(); }
    public void Start() { this.IsActive = true; }
    public void Suspend() { }
    public void Tick(long now, bool preview, Func<bool> combat)
    {
        this.Ticks++;
        if (this.EndOnTick) this.IsActive = false;
        else if (!this.Allow || !combat()) this.Moves++;
    }
    public void DrawOverlay() { this.Draws++; }
    public void CombatAccepted(int milliseconds) { this.PausedMilliseconds += milliseconds; }
    public void Stop() { this.IsActive = false; }
}
