namespace wa_api.Infrastructure.Caching;

public record IdempotencyResult(int StatusCode, string ResponseJson);

public interface IIdempotencyService
{
    Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default);
    Task StoreAsync(string key, int statusCode, string responseJson, CancellationToken ct = default);
}
