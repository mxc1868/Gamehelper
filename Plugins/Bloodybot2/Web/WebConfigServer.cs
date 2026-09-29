namespace Bloodybot2.Web;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bloodybot2.Configuration;
using Bloodybot2.Runtime;

public sealed class WebConfigServer : IDisposable
{
    private const int MaxBody = 256 * 1024;
    private HttpListener listener = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly object sync = new();
    private readonly ConfigStore store;
    private readonly BotRuntime runtime;
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, (byte[] Bytes, string Type)> assets = new();
    private BotConfig config;
    private long revision = 1;
    private bool disposed;
    public string Url { get; private set; } = "";

    public WebConfigServer(ConfigStore store, BotRuntime runtime)
    {
        this.store = store;
        this.runtime = runtime;
        this.config = store.Load();
        runtime.Apply(this.config, this.revision);
        foreach (var (name, type) in new[] { ("index.html", "text/html"), ("app.js", "text/javascript"), ("styles.css", "text/css") })
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Bloodybot2.Web.wwwroot." + name)
                ?? throw new InvalidOperationException("Missing web asset: " + name);
            using var memory = new MemoryStream();
            resource.CopyTo(memory);
            this.assets["/" + name] = (memory.ToArray(), type + "; charset=utf-8");
        }
    }

    public void Start(int firstPort = 38432)
    {
        for (var port = firstPort; port < firstPort + 8; port++)
        {
            this.listener.Prefixes.Clear();
            this.Url = $"http://127.0.0.1:{port}/";
            this.listener.Prefixes.Add(this.Url);
            try { this.listener.Start(); _ = this.Listen(); return; }
            catch (HttpListenerException) when (port < firstPort + 7)
            {
                // A failed Start can dispose HttpListener on Windows. A new
                // listener is required before trying the next loopback port.
                this.listener.Close();
                this.listener = new();
            }
        }
    }

    private async Task Listen()
    {
        try
        {
            while (!this.stopping.IsCancellationRequested)
            {
                var context = await this.listener.GetContextAsync().WaitAsync(this.stopping.Token).ConfigureAwait(false);
                _ = this.Handle(context);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
    }

    private object Bootstrap() => new { config = this.config, revision = this.revision, token = this.token, warning = this.store.Warning };

    private async Task Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        try
        {
            var origin = this.Url.TrimEnd('/');
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address) ||
                !string.Equals(request.Headers["Host"], new Uri(this.Url).Authority, StringComparison.OrdinalIgnoreCase) ||
                request.Headers["Origin"] is string requestOrigin && !string.Equals(requestOrigin, origin, StringComparison.Ordinal))
            { await this.Reply(response, 403, new { error = "仅允许本机配置页面访问。" }); return; }
            var path = request.Url?.AbsolutePath ?? "/";
            if (request.HttpMethod == "GET")
            {
                if (path == "/api/bootstrap")
                {
                    string body;
                    lock (this.sync) body = JsonSerializer.Serialize(this.Bootstrap(), BotConfig.Json);
                    await Write(response, 200, Encoding.UTF8.GetBytes(body), "application/json; charset=utf-8");
                }
                else if (path == "/api/status") await this.Reply(response, 200, this.runtime.Status());
                else if (this.assets.TryGetValue(path == "/" ? "/index.html" : path, out var asset)) await Write(response, 200, asset.Bytes, asset.Type);
                else await this.Reply(response, 404, new { error = "页面不存在。" });
                return;
            }
            if (request.Headers["X-Bloodybot2-Token"] != this.token)
            { await this.Reply(response, 403, new { error = "配置会话已失效，请重新加载页面。" }); return; }
            if (path == "/api/config" && request.HttpMethod == "PUT")
            {
                var body = await this.ReadBody(request);
                var save = JsonSerializer.Deserialize<SaveRequest>(body, BotConfig.Json) ?? throw new ArgumentException("缺少配置。");
                if (save.Config == null) throw new ArgumentException("缺少配置。");
                save.Config.Validate();
                string result;
                var code = 200;
                lock (this.sync)
                {
                    if (this.disposed) throw new ObjectDisposedException(nameof(WebConfigServer));
                    if (save.Revision != this.revision)
                    {
                        code = 409;
                        result = JsonSerializer.Serialize(new { error = "另一个页面已保存配置，请先导出当前草稿，再重新加载。" }, BotConfig.Json);
                    }
                    else
                    {
                        // Publish only after durable save. Failure leaves both the live config and revision unchanged.
                        this.store.Save(save.Config);
                        this.config = save.Config;
                        this.revision++;
                        this.runtime.Apply(this.config, this.revision);
                        result = JsonSerializer.Serialize(this.Bootstrap(), BotConfig.Json);
                    }
                }
                await Write(response, code, Encoding.UTF8.GetBytes(result), "application/json; charset=utf-8");
            }
            else if (path == "/api/run" && request.HttpMethod == "POST")
            {
                var command = JsonSerializer.Deserialize<RunRequest>(await this.ReadBody(request), BotConfig.Json)
                    ?? throw new ArgumentException("缺少运行命令。");
                lock (this.sync)
                {
                    if (this.disposed) throw new ObjectDisposedException(nameof(WebConfigServer));
                    if (command.Running) this.runtime.Start(Environment.TickCount64);
                    else this.runtime.Stop("网页已停止");
                }
                await this.Reply(response, 200, this.runtime.Status());
            }
            else await this.Reply(response, 404, new { error = "接口不存在。" });
        }
        catch (BodyTooLargeException) { await this.TryError(response, 413, "配置超过 256 KiB。"); }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { await this.TryError(response, 422, ex.Message); }
        catch (OperationCanceledException) { await this.TryError(response, 408, "请求超时。"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpListenerException or ObjectDisposedException)
        { await this.TryError(response, 500, "读写失败，配置未确认保存：" + ex.Message); }
        finally { response.Close(); }
    }

    private async Task<string> ReadBody(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxBody) throw new BodyTooLargeException();
        if (!(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请求必须使用 application/json。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(this.stopping.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = await request.InputStream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaxBody) throw new BodyTooLargeException();
            buffer.Write(chunk, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private Task Reply(HttpListenerResponse response, int code, object body) =>
        Write(response, code, JsonSerializer.SerializeToUtf8Bytes(body, BotConfig.Json), "application/json; charset=utf-8");
    private async Task TryError(HttpListenerResponse response, int code, string error)
    {
        try { await this.Reply(response, code, new { error }); }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException) { }
    }
    private static async Task Write(HttpListenerResponse response, int code, byte[] bytes, string type)
    {
        response.StatusCode = code;
        response.ContentType = type;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }
    public void Dispose()
    {
        lock (this.sync)
        {
            this.disposed = true;
            this.runtime.Stop("插件已禁用");
            this.stopping.Cancel();
            this.listener.Close();
        }
    }
    private sealed record SaveRequest(long Revision, BotConfig? Config);
    private sealed record RunRequest(bool Running);
    private sealed class BodyTooLargeException : Exception;
}
