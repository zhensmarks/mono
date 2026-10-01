using System.Net;
using System.Net.Http.Headers;

namespace BMachine.UI.Services;

/// <summary>Centralizes Trello authentication and protects credentials from data-derived URLs.</summary>
public static class TrelloRequestSecurity
{
    private const string ApiHost = "api.trello.com";
    private const string WebHost = "trello.com";
    private const int MaxRedirects = 5;
    private const int MaxMediaBytes = 20_000_000;

    public static HttpClient CreateApiClient(string apiKey, string token, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var transport = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false
        };
        var client = new HttpClient(new ApiOnlyAuthenticationHandler(apiKey, token) { InnerHandler = transport })
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
        return client;
    }

    /// <summary>
    /// Downloads an image from an HTTPS URL. Trello auth is sent only on the initial
    /// request to an exact Trello-owned host; redirects are followed without auth.
    /// </summary>
    public static async Task<byte[]?> DownloadMediaAsync(
        string? rawUrl,
        string? apiKey = null,
        string? token = null,
        int maximumBytes = MaxMediaBytes,
        CancellationToken cancellationToken = default)
    {
        maximumBytes = Math.Clamp(maximumBytes, 1, 250_000_000);
        if (!TryNormalizeMediaUri(rawUrl, out var current)) return null;

        using var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("BMachine/1.0");
            if (redirectCount == 0 && !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(token) && IsTrustedMediaHost(current))
                request.Headers.Authorization = CreateOAuthHeader(apiKey, token);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount == MaxRedirects || response.Headers.Location is null) return null;
                var next = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current, response.Headers.Location);
                if (!TryNormalizeMediaUri(next.AbsoluteUri, out current)) return null;
                continue;
            }

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maximumBytes) return null;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (count == 0) break;
                if (output.Length + count > maximumBytes) return null;
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            return output.ToArray();
        }

        return null;
    }

    public static bool TryNormalizeMediaUri(string? rawUrl, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
            return false;

        try
        {
            var builder = new UriBuilder(parsed);
            var query = parsed.Query.TrimStart('?');
            if (!string.IsNullOrEmpty(query))
            {
                var safeParameters = new List<string>();
                foreach (var parameter in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var equalsIndex = parameter.IndexOf('=');
                    var name = equalsIndex < 0 ? parameter : parameter[..equalsIndex];
                    string decodedName;
                    try { decodedName = Uri.UnescapeDataString(name.Replace('+', ' ')); }
                    catch { decodedName = name; }
                    if (decodedName.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                        decodedName.Equals("token", StringComparison.OrdinalIgnoreCase))
                        continue;
                    safeParameters.Add(parameter);
                }
                builder.Query = string.Join('&', safeParameters);
            }
            else
            {
                builder.Query = string.Empty;
            }
            uri = builder.Uri;
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool IsTrustedMediaHost(Uri uri) =>
        uri.IsDefaultPort &&
        (uri.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals(WebHost, StringComparison.OrdinalIgnoreCase));

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static AuthenticationHeaderValue CreateOAuthHeader(string apiKey, string token)
    {
        static string Escape(string value)
        {
            if (value.Any(char.IsControl)) throw new ArgumentException("Credential contains an invalid control character.");
            return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        return new AuthenticationHeaderValue("OAuth",
            $"oauth_consumer_key=\"{Escape(apiKey)}\", oauth_token=\"{Escape(token)}\"");
    }

    private sealed class ApiOnlyAuthenticationHandler : DelegatingHandler
    {
        private readonly AuthenticationHeaderValue _authorization;

        public ApiOnlyAuthenticationHandler(string apiKey, string token)
        {
            _authorization = CreateOAuthHeader(apiKey, token);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri is null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
                !string.IsNullOrEmpty(uri.UserInfo) || !uri.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Trello credentials may only be sent to the HTTPS Trello API origin.");

            if (!TryNormalizeMediaUri(uri.AbsoluteUri, out var sanitized))
                throw new InvalidOperationException("The Trello API URL is invalid.");

            request.RequestUri = sanitized;
            request.Headers.Authorization = _authorization;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
