using System.Text.Json;
using chatrealtime.Models;

namespace chatrealtime.Services;

public interface IOpenAILiveClient
{
    Task<LiveCreateResponse> CreateSessionAsync(LiveCreateRequest request, CancellationToken cancellationToken);
    Task<ILiveSidebandConnection> AttachSidebandAsync(string sessionId, CancellationToken cancellationToken);
}

public interface ILiveSidebandConnection : IAsyncDisposable
{
    Task SendAsync(JsonElement message, CancellationToken cancellationToken);
    Task RunAsync(Func<JsonElement, Task> onEvent, Func<Task> onClosed, CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
}

public sealed class LiveApiException : Exception
{
    public int StatusCode { get; }

    public LiveApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
