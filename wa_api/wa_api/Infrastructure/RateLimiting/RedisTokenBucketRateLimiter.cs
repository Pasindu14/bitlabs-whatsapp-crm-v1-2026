using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace wa_api.Infrastructure.RateLimiting;

/// <summary>
/// Redis token-bucket limiter, atomic across all worker replicas via a single Lua script. Time is read from
/// Redis (<c>TIME</c>) so replica clock skew can't drift the refill. On any Redis error it degrades to a
/// bounded in-process bucket (shared <see cref="LocalBucketRegistry"/>) at the conservative fallback rate —
/// it never fails open. Mirrors the EVAL pattern used by <c>RedisDistributedLockService</c>.
/// </summary>
public sealed class RedisTokenBucketRateLimiter : IWabaRateLimiter
{
    private readonly IDatabase _db;
    private readonly LocalBucketRegistry _fallback;
    private readonly RateLimitOptions _opts;
    private readonly ILogger<RedisTokenBucketRateLimiter> _logger;

    public RedisTokenBucketRateLimiter(
        IConnectionMultiplexer redis,
        LocalBucketRegistry fallback,
        IOptions<RateLimitOptions> options,
        ILogger<RedisTokenBucketRateLimiter> logger)
    {
        _db = redis.GetDatabase();
        _fallback = fallback;
        _opts = options.Value;
        _logger = logger;
    }

    // Token bucket. KEYS[1]=bucket hash. ARGV: capacity, refillPerSec, want, ttlSeconds.
    // Returns { allowed(1/0), retryAfterMs }.
    private const string BucketScript = @"
local cap    = tonumber(ARGV[1])
local refill = tonumber(ARGV[2])
local want   = tonumber(ARGV[3])
local ttl    = tonumber(ARGV[4])
local t   = redis.call('TIME')
local now = tonumber(t[1]) + tonumber(t[2]) / 1000000.0
local d      = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
local tokens = tonumber(d[1])
local ts     = tonumber(d[2])
if tokens == nil then tokens = cap; ts = now end
tokens = math.min(cap, tokens + math.max(0, now - ts) * refill)
local allowed = 0
local retry = 0
if tokens >= want then
  tokens = tokens - want
  allowed = 1
else
  retry = math.ceil(((want - tokens) / refill) * 1000)
end
redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now)
redis.call('PEXPIRE', KEYS[1], ttl * 1000)
return { allowed, retry }";

    // Daily unique-recipient tier cap (business-initiated). KEYS[1]=day set. ARGV: contactId, limit, ttlSeconds.
    // Returns 1 (allowed) or 0 (over cap). Idempotent: a recipient already counted today is always allowed,
    // so retrying the same recipient never double-counts.
    private const string DailyCapScript = @"
if redis.call('SISMEMBER', KEYS[1], ARGV[1]) == 1 then return 1 end
if redis.call('SCARD', KEYS[1]) >= tonumber(ARGV[2]) then return 0 end
redis.call('SADD', KEYS[1], ARGV[1])
redis.call('EXPIRE', KEYS[1], tonumber(ARGV[3]))
return 1";

    public async Task<RateLimitResult> TryAcquireAsync(
        string phoneNumberId, Guid? recipientContactId, int dailyTierLimit, CancellationToken ct = default)
    {
        try
        {
            var raw = await _db.ScriptEvaluateAsync(
                BucketScript,
                new RedisKey[] { $"rl:tb:{phoneNumberId}" },
                new RedisValue[] { _opts.PerSecondCapacity, _opts.PerSecondRefill, 1, _opts.BucketTtlSeconds });

            var arr = (RedisResult[]?)raw;
            var allowed = arr is not null && (long)arr[0] == 1;
            if (!allowed)
            {
                var retryMs = arr is not null ? (long)arr[1] : 100;
                return new RateLimitResult(false, TimeSpan.FromMilliseconds(retryMs), RateLimitReason.PerSecondThrottle);
            }

            // Per-24h unique-recipient tier cap — only for business-initiated sends (campaigns pass a
            // contact id + a positive tier). Session/service replies pass null/0 and skip it.
            if (recipientContactId is Guid contactId && dailyTierLimit > 0)
            {
                // NOTE: keyed by UTC CALENDAR day, not Meta's rolling 24h window. Just after midnight UTC a
                // fresh tier budget opens while Meta may still be counting yesterday — so this can slightly
                // UNDER-protect at the boundary. Acceptable: Meta is the authoritative cap; this is a proactive
                // guard. A true rolling window would need a sorted-set of per-send timestamps.
                var dayKey = $"rl:day:{phoneNumberId}:{DateTime.UtcNow:yyyyMMdd}";
                var dailyRaw = await _db.ScriptEvaluateAsync(
                    DailyCapScript,
                    new RedisKey[] { dayKey },
                    new RedisValue[] { contactId.ToString(), dailyTierLimit, _opts.DailySetTtlSeconds });

                if ((long)dailyRaw == 0)
                    return new RateLimitResult(false, TimeSpan.Zero, RateLimitReason.DailyTierExceeded);
            }

            return RateLimitResult.Allow;
        }
        catch (RedisServerException ex)
        {
            // A SERVER/SCRIPT error is a bug (malformed Lua, bad return type), not a transient outage — and the
            // fallback below would otherwise hide it forever. Log at Error so a broken pacer is visible, then
            // still degrade safely rather than fail the send.
            _logger.LogError(ex,
                "Redis rate-limit SCRIPT error for {Phone} — token-bucket Lua is likely broken; degrading to in-process fallback.",
                phoneNumberId);
            return _fallback.TryTake($"fb:{phoneNumberId}", _opts.FallbackRefill, _opts.FallbackRefill);
        }
        catch (Exception ex)
        {
            // Transient (connection/timeout). FAIL-SAFE (never fail-open): degrade to a bounded per-process
            // bucket at the low fallback rate.
            _logger.LogWarning(ex,
                "Redis rate-limit call failed for {Phone} (transient); degrading to in-process fallback bucket.",
                phoneNumberId);
            return _fallback.TryTake($"fb:{phoneNumberId}", _opts.FallbackRefill, _opts.FallbackRefill);
        }
    }
}
