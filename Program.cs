using chatrealtime.Configuration;
using chatrealtime.Services;
using chatrealtime.Services.Tools;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenAI settings from appsettings.json
var openAISettings = builder.Configuration.GetSection("OpenAI").Get<OpenAISettings>() ?? new OpenAISettings();
builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Configure HttpClient with Polly resilience policies
var resilienceSettings = openAISettings.Resilience;

builder.Services.AddHttpClient("ToolsHttpClient")
    .AddPolicyHandler((services, request) =>
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        // Build the pipeline of policies
        var policies = new List<IAsyncPolicy<HttpResponseMessage>>();

        // 1. Timeout policy (innermost)
        if (resilienceSettings.Timeout.Enabled)
        {
            var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(resilienceSettings.Timeout.TimeoutSeconds),
                TimeoutStrategy.Optimistic,
                onTimeoutAsync: (context, timeout, task) =>
                {
                    logger.LogWarning("[Polly Timeout] Request timed out after {Timeout}s", timeout.TotalSeconds);
                    return Task.CompletedTask;
                });
            policies.Add(timeoutPolicy);
        }

        // 2. Retry policy (middle layer)
        if (resilienceSettings.Retry.Enabled)
        {
            var retryPolicy = HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .WaitAndRetryAsync(
                    retryCount: resilienceSettings.Retry.MaxRetryAttempts,
                    sleepDurationProvider: retryAttempt =>
                    {
                        var delay = Math.Min(
                            resilienceSettings.Retry.InitialDelayMs * Math.Pow(2, retryAttempt - 1),
                            resilienceSettings.Retry.MaxDelayMs
                        );
                        return TimeSpan.FromMilliseconds(delay);
                    },
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        logger.LogWarning(
                            "[Polly Retry] Retry {RetryAttempt}/{MaxRetries} after {Delay}ms. Reason: {Reason}",
                            retryAttempt,
                            resilienceSettings.Retry.MaxRetryAttempts,
                            timespan.TotalMilliseconds,
                            outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString() ?? "Unknown"
                        );
                    });
            policies.Add(retryPolicy);
        }

        // 3. Circuit Breaker policy (outermost)
        if (resilienceSettings.CircuitBreaker.Enabled)
        {
            var circuitBreakerPolicy = HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .AdvancedCircuitBreakerAsync(
                    failureThreshold: 0.5, // 50% failure rate
                    samplingDuration: TimeSpan.FromSeconds(resilienceSettings.CircuitBreaker.SamplingDurationSeconds),
                    minimumThroughput: resilienceSettings.CircuitBreaker.FailureThreshold,
                    durationOfBreak: TimeSpan.FromSeconds(resilienceSettings.CircuitBreaker.BreakDurationSeconds),
                    onBreak: (result, breakDelay) =>
                    {
                        logger.LogError(
                            "[Polly Circuit Breaker] Circuit opened for {BreakDuration}s. Reason: {Reason}",
                            breakDelay.TotalSeconds,
                            result.Exception?.Message ?? result.Result?.StatusCode.ToString() ?? "Unknown"
                        );
                    },
                    onReset: () =>
                    {
                        logger.LogInformation("[Polly Circuit Breaker] Circuit reset (closed)");
                    },
                    onHalfOpen: () =>
                    {
                        logger.LogInformation("[Polly Circuit Breaker] Circuit half-open (testing)");
                    });
            policies.Add(circuitBreakerPolicy);
        }

        // Combine all policies into a single policy wrap (outer to inner: CB -> Retry -> Timeout)
        if (policies.Count == 0)
        {
            return Policy.NoOpAsync<HttpResponseMessage>();
        }
        else if (policies.Count == 1)
        {
            return policies[0];
        }
        else
        {
            // Reverse to wrap correctly: CircuitBreaker wraps Retry wraps Timeout
            policies.Reverse();
            return Policy.WrapAsync(policies.ToArray());
        }
    });

builder.Services.AddTransient<OpenAIRealtimeService>();
builder.Services.AddSingleton<RealtimeWebSocketHandler>();
builder.Services.AddSingleton<IToolExecutor, ToolExecutorService>();

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

// Map API controllers
app.MapControllers();

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
