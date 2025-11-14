using System;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Services;

namespace 币安量化机器人.Services.Health;

public class HealthCheckService
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public HealthCheckService()
    {
    }

    public async Task<HealthCheckResult> RunAsync(CancellationToken ct = default)
    {
        var result = new HealthCheckResult
        {
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // Check configuration: look for Binance API key in configuration or environment
            string? apiKey = null;
            try
            {
                apiKey = ConfigurationService.GetValue("Api:Binance:ApiKey");
            }
            catch
            {
                apiKey = null;
            }

            var envKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            result.ConfigOk = !string.IsNullOrWhiteSpace(apiKey ?? envKey);

            // Check API reachable by calling a lightweight public endpoint
            try
            {
                var apiClient = ServiceLocator.Api;
                var tickers = await apiClient.GetMiniTickersAsync(null, ct).ConfigureAwait(false);
                result.ApiOk = tickers != null && tickers.Count > 0;
            }
            catch (Exception ex)
            {
                result.ApiOk = false;
                result.AddNote($"API check failed: {ex.Message}");
            }

            // Check cache initialized
            try
            {
                var cache = ServiceLocator.Cache;
                result.CacheOk = cache != null;
            }
            catch (Exception ex)
            {
                result.CacheOk = false;
                result.AddNote($"Cache check failed: {ex.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            result.AddNote("Health check cancelled");
        }
        catch (Exception ex)
        {
            result.AddNote($"Health check exception: {ex.Message}");
        }

        return result;
    }
}

public class HealthCheckResult
{
    public DateTime Timestamp { get; set; }
    public bool ConfigOk { get; set; }
    public bool ApiOk { get; set; }
    public bool CacheOk { get; set; }

    private readonly System.Collections.Generic.List<string> _notes = new();
    public string[] Notes => _notes.ToArray();

    public void AddNote(string note) => _notes.Add(note);

    public bool IsHealthy => ConfigOk && ApiOk && CacheOk;
}
