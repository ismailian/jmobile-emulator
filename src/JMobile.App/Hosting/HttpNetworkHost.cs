using JMobile.Runtime.Host;

namespace JMobile.App.Hosting;

/// <summary>
/// Outbound HTTP for the Generic Connection Framework.
/// </summary>
/// <remarks>
/// Disabled unless the user turns it on. The servers these games talk to are long gone, MIDP's
/// networking API blocks the calling thread, and the calling thread is frequently the one
/// painting the screen — so a request that cannot succeed shows up as a frozen game. A short
/// timeout keeps even an enabled request from stalling a session for long.
/// </remarks>
public sealed class HttpNetworkHost : INetworkHost, IDisposable
{
    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public bool IsEnabled { get; set; }

    public HttpResult Request(HttpRequest request)
    {
        if (!IsEnabled)
            return new HttpResult(0, "Network access is disabled", [], []);

        try
        {
            using var message = new HttpRequestMessage(
                new HttpMethod(request.Method), request.Url);

            foreach (KeyValuePair<string, string> header in request.Headers)
            {
                // Content headers have to go on the body, and the client rejects them elsewhere.
                if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value) &&
                    message.Content is not null)
                {
                    message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (request.Body.Length > 0)
            {
                message.Content = new ByteArrayContent(request.Body);
                foreach (KeyValuePair<string, string> header in request.Headers)
                {
                    if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                        message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // The MIDP API is synchronous, so this call is too. It runs on whichever guest
            // thread made the request, never on the emulator's own event thread.
            using HttpResponseMessage response = _client.Send(message);

            var headers = new List<KeyValuePair<string, string>>();
            foreach (var header in response.Headers)
                headers.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));
            foreach (var header in response.Content.Headers)
                headers.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));

            using var buffer = new MemoryStream();
            response.Content.ReadAsStream().CopyTo(buffer);

            return new HttpResult(
                (int)response.StatusCode,
                response.ReasonPhrase ?? response.StatusCode.ToString(),
                headers,
                buffer.ToArray());
        }
        catch (Exception error)
        {
            return new HttpResult(0, error.Message, [], []);
        }
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Vibration and backlight, mapped onto what a desktop can actually do.</summary>
public sealed class DesktopFeedbackHost : IDeviceFeedbackHost
{
    /// <summary>Raised when a game asks the handset to vibrate, so the shell can show it.</summary>
    public event Action<int>? VibrateRequested;

    /// <summary>Raised when a game flashes the backlight.</summary>
    public event Action<int>? BacklightRequested;

    public void Vibrate(int durationMilliseconds) => VibrateRequested?.Invoke(durationMilliseconds);

    public void FlashBacklight(int durationMilliseconds) => BacklightRequested?.Invoke(durationMilliseconds);
}
