using System.Net;

namespace PlaySpace.SupportBot;

internal static class BotTransport
{
    internal static HttpClientHandler CreateHandler(string? proxyUrl)
    {
        // Explicit route: never silently inherit a VPN client's system proxy.
        var handler = new HttpClientHandler { UseProxy = false };
        if (string.IsNullOrWhiteSpace(proxyUrl)) return handler;
        if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "socks5") ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.AbsolutePath != "/" && uri.AbsolutePath != "" ||
            uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            handler.Dispose();
            throw new IOException("Set proxyUrl to an HTTP or SOCKS5 proxy URL. MTProto links are not supported.");
        }
        var address = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
        var proxy = new WebProxy(address);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]),
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }
        handler.Proxy = proxy;
        handler.UseProxy = true;
        return handler;
    }

    internal static HttpClient CreateHttp(string? proxyUrl) => new(CreateHandler(proxyUrl)) { Timeout = TimeSpan.FromSeconds(50) };

    internal static async Task<bool> Check(Configuration configuration, CancellationToken cancellation)
    {
        using var http = CreateHttp(configuration.ProxyUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await http.GetAsync($"https://api.telegram.org/bot{configuration.Token}/getMe", timeout.Token);
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            bool valid = response.IsSuccessStatusCode && json.RootElement.GetProperty("ok").GetBoolean() &&
                string.Equals(json.RootElement.GetProperty("result").GetProperty("username").GetString(), "PlaySpaceSupport_bot", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine(valid ? "Telegram connection verified: @PlaySpaceSupport_bot." : "Telegram rejected the credentials or returned an unexpected bot. Check the private token.");
            return valid;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // Exception messages can contain the token-bearing request URL or proxy credentials.
            Console.WriteLine("Telegram connection unavailable. Check the proxy address, port, credentials and connectivity.");
            return false;
        }
    }
}
