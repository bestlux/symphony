using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Symphony.Operator.Models;

namespace Symphony.Operator.Services;

public sealed class SymphonyApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public SymphonyApiClient(string baseUrl = "http://127.0.0.1:4027")
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<SymphonyStateDto?> GetStateAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<SymphonyStateDto>("/api/v1/state", JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HealthDto?> GetHealthAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<HealthDto>("/api/v1/health", JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync("/api/v1/refresh", null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopRunAsync(string issueId, bool cleanupWorkspace, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new { cleanup_workspace = cleanupWorkspace }, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync($"/api/v1/runs/{Uri.EscapeDataString(issueId)}/stop", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task RetryRunAsync(string issueId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"/api/v1/runs/{Uri.EscapeDataString(issueId)}/retry", null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> GetRecentLogsAsync(int count, CancellationToken cancellationToken)
    {
        var logs = await _httpClient.GetFromJsonAsync<RecentLogsDto>($"/api/v1/logs/recent?count={count}", JsonOptions, cancellationToken).ConfigureAwait(false);
        return logs?.Lines ?? [];
    }
}
