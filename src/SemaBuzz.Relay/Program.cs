using SemaBuzz.Relay;

// ──────────────────────────────────────────────────────────────────────────────
// SemaBuzz Relay Server  (ASP.NET Core WebSocket relay)
//
// Hosting:
//   Railway / Render / Fly.io — set PORT env var; TLS terminated by platform.
//   Self-hosted             — run behind nginx/Caddy for HTTPS.
//
// Usage:
//   dotnet run                        ← defaults to PORT env var or 7171
//   dotnet run -- --port 8080
// ──────────────────────────────────────────────────────────────────────────────

var portStr = Environment.GetEnvironmentVariable("PORT");
var port    = int.TryParse(portStr, out var p) ? p : 7171;

for (var i = 0; i < args.Length - 1; i++)
    if ((args[i] == "--port" || args[i] == "-p") && int.TryParse(args[i + 1], out var ap))
        port = ap;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning); // quiet in production

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

var relay = new RelayServer();

// WebSocket endpoint: clients connect here to join a relay room.
app.Map("/relay", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 426;
        await ctx.Response.WriteAsync("WebSocket upgrade required.");
        return;
    }
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await relay.HandleClientAsync(ws, ctx.RequestAborted);
});

// Health check for Railway / Render uptime monitors.
app.MapGet("/", () => Results.Ok("SemaBuzz Relay OK"));

Console.WriteLine($"SemaBuzz Relay | port {port}  |  /relay");
await app.RunAsync();

