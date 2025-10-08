using chatrealtime.Configuration;
using chatrealtime.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenAI settings from appsettings.json
builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddTransient<OpenAIRealtimeService>();
builder.Services.AddSingleton<RealtimeWebSocketHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Enable WebSocket support
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
});

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

// WebSocket endpoint for realtime communication
app.Map("/ws/realtime", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var handler = context.RequestServices.GetRequiredService<RealtimeWebSocketHandler>();
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await handler.HandleWebSocketAsync(context, webSocket);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.Run();
