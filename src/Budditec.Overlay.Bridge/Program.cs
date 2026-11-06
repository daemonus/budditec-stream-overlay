using Budditec.Overlay.Bridge;
using Budditec.Overlay.Core.Settings;

// Local-only bridge for the web (Tauri) prototype: serves the overlay pages and streams chat/OBS events
// to them over a WebSocket. Binds to loopback so nothing is exposed to the network.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:47820");
builder.Services.AddSingleton<BridgeHub>();
builder.Services.AddSingleton(new SettingsStore());
builder.Services.AddSingleton<OverlayServices>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OverlayServices>());

var app = builder.Build();
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

app.Map("/ws", async (HttpContext context, BridgeHub hub, OverlayServices services) =>
{
    if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest();
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.RunClientAsync(socket, [services.SettingsMessage, services.ObsMessage], context.RequestAborted);
    return Results.Empty;
});

app.MapPost("/edit/{on:bool}", (bool on, BridgeHub hub) =>
{
    hub.Broadcast(new { type = "edit", on });
    return Results.NoContent();
});

app.MapPost("/test-message", (OverlayServices services) =>
{
    services.SendTestMessage();
    return Results.NoContent();
});

app.Run();
