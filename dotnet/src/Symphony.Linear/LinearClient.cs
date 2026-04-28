using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Symphony.Linear;

public sealed class LinearClient
{
    private const int MaxErrorBodyBytes = 1_000;
    private readonly HttpClient _httpClient;
    private readonly Func<LinearOptions> _optionsFactory;

    public LinearClient(HttpClient httpClient, LinearOptions options)
        : this(httpClient, () => options)
    {
    }

    public LinearClient(HttpClient httpClient, ILinearOptionsProvider optionsProvider)
        : this(httpClient, optionsProvider.GetLinearOptions)
    {
    }

    public LinearClient(HttpClient httpClient, Func<LinearOptions> optionsFactory)
    {
        _httpClient = httpClient;
        _optionsFactory = optionsFactory;
    }

    public async Task<JsonDocument> GraphQlAsync(string query, JsonObject? variables, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("GraphQL query is required.", nameof(query));
        }

        var options = _optionsFactory().ResolveEnvironment();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new LinearException("Linear API token is missing. Set tracker.api_key or LINEAR_API_KEY.");
        }

        var payload = new JsonObject
        {
            ["query"] = query,
            ["variables"] = variables ?? new JsonObject()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", options.ApiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var summary = SummarizeBody(body);
            throw new LinearException($"Linear GraphQL request failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body={summary}");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new LinearException("Linear GraphQL response was not valid JSON.", ex);
        }
    }

    private static string SummarizeBody(string body)
    {
        var normalized = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaxErrorBodyBytes ? normalized : normalized[..MaxErrorBodyBytes] + "...<truncated>";
    }
}

