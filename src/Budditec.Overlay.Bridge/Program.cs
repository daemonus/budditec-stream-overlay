using Budditec.Overlay.Bridge;
using Budditec.Overlay.Core;
using Budditec.Overlay.Core.Settings;

// Local-only bridge for the web (Tauri) prototype: serves the overlay and settings pages, streams chat/OBS
// events to them over a WebSocket, and reads/writes settings. Binds to loopback so nothing is exposed to
// the network.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:47820");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    foreach (var converter in BridgeHub.JsonOptions.Converters) o.SerializerOptions.Converters.Add(converter);
});
builder.Services.AddSingleton<BridgeHub>();
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILogger<OverlayRuntime>>();
    return new OverlayRuntime(new SettingsStore(), message => log.LogInformation("{Message}", message));
});
builder.Services.AddSingleton<OverlayServices>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OverlayServices>());

var app = builder.Build();

// Browsers send Origin on cross-site requests and WebSocket handshakes. Only our own pages may use the
// bridge, so another website open in the user's browser can't read chat or change settings.
app.Use(async (context, next) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    if (origin.Length > 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
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

app.MapGet("/api/settings", (OverlayRuntime runtime) => new
{
    settings = runtime.Settings,
    widgets = OverlayWidgets.All.Select(w => new { id = w.Id, name = w.Name }),
});

app.MapPut("/api/settings", (OverlaySettings settings, OverlayRuntime runtime) =>
{
    // Widget bounds are saved by the shell when editing the layout; don't overwrite them with a stale copy.
    settings.Widgets = runtime.Settings.Widgets;
    return runtime.Apply(settings) is { } error
        ? Results.BadRequest(new { error })
        : Results.Ok(new { settings = runtime.Settings });
});

app.MapPost("/edit/{on:bool}", (bool on, BridgeHub hub) =>
{
    hub.Broadcast(new { type = "edit", on });
    return Results.NoContent();
});

app.MapPost("/test-message", (OverlayRuntime runtime) =>
{
    runtime.SendTestMessage();
    return Results.NoContent();
});

app.Run();
