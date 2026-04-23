using SemaBuzz.Relay;

// SemaBuzz Relay Server  (ASP.NET Core WebSocket relay)
//
// Hosting:
//   Railway / Render / Fly.io  set PORT env var; TLS terminated by platform.
//   Self-hosted              run behind nginx/Caddy for HTTPS.
//
// Usage:
//   dotnet run                        ← defaults to PORT env var or 7171
//   dotnet run -- --port 8080
//   SemaBuzz-Relay-Windows.exe --port 8080
//   ./SemaBuzz-Relay-Linux --port 8080
//
// Stopping:
//   Press Ctrl+C in this terminal window for a clean shutdown.
//   Windows background: Stop-Process -Name "SemaBuzz-Relay-Windows"
//   Linux background:   pkill SemaBuzz-Relay-Linux
//   Docker:             docker stop <container-name>

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
    // Prefer X-Forwarded-For set by Railway's reverse proxy; fall back to direct IP.
    var remoteIp = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                   ?? ctx.Connection.RemoteIpAddress?.ToString()
                   ?? "unknown";
    await relay.HandleClientAsync(ws, remoteIp, ctx.RequestAborted);
});

// Health check for Railway / Render uptime monitors.
app.MapGet("/", () => Results.Ok("SemaBuzz Relay OK"));

Console.WriteLine($"SemaBuzz Relay | port {port}  |  /relay");
Console.WriteLine("MIT License — Copyright (c) 2026 Skynr Labs");
Console.WriteLine("This relay is a blind pass-through. It does not log, read, or store message content.");
Console.WriteLine("IP addresses are held in memory only for the duration of a session.");
Console.WriteLine("Press Ctrl+C to stop.");
await app.RunAsync();

