using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using TerminalManagementService.Models;

namespace TerminalManagementService.Services;

public class RedisTerminalService : ITerminalService
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ConnectionMultiplexer _releaseRedis;
    private readonly IDatabase _releaseDb;
    private readonly ILogger<RedisTerminalService> _logger;
    private readonly TerminalConfiguration _config;
    private readonly IConfiguration _appConfig;
    private readonly string _podId;
    private readonly ConcurrentDictionary<string, TerminalInfo> _terminalInfoCache;

    // Redis key patterns - we no longer need TerminalInfoKeyPattern as we store config in appsettings
    private const string TerminalStatusKeyPattern = "terminal:status:{0}";
    private const string TerminalSessionKeyPattern = "terminal:session:{0}";
    private const string TerminalQueueKey = "terminal_queue";

    public RedisTerminalService(
        ConnectionMultiplexer redis,
        ConnectionMultiplexer releaseRedis,
        IOptions<TerminalConfiguration> config,
        IConfiguration appConfig,
        ILogger<RedisTerminalService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _db = _redis.GetDatabase();
        _releaseRedis = releaseRedis ?? throw new ArgumentNullException(nameof(releaseRedis));
        _releaseDb = _releaseRedis.GetDatabase();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _terminalInfoCache = new ConcurrentDictionary<string, TerminalInfo>();

        // Get pod name from environment variable (set by Kubernetes) or from config
        _podId = Environment.GetEnvironmentVariable("POD_NAME") ?? _config.PodName;

        _logger.LogInformation("RedisTerminalService initialized with terminal info caching enabled");
    }

    /// <summary>
    /// Initialize terminals in Redis based on configuration
    /// </summary>
    public async Task InitializeTerminalsAsync()
    {
        try
        {
            // Get terminals data from configuration
            var terminalsData = _appConfig.GetSection("TerminalsData").Get<string[]>();
            if (terminalsData == null || terminalsData.Length == 0)
            {
                _logger.LogWarning("No terminals data found in configuration. Skipping initialization.");
                return;
            }

            _logger.LogInformation("Found {Count} terminals in configuration", terminalsData.Length);

            foreach (var terminalDataString in terminalsData)
            {
                // Parse terminal data string
                // Format: Address|Port|Username|Password|TerminalId|Branch
                var parts = terminalDataString.Split('|');
                if (parts.Length < 6)
                {
                    _logger.LogWarning("Invalid terminal data format for entry: {Data}", terminalDataString);
                    continue;
                }

                // Extract terminal ID from the data string (5th element)
                if (!int.TryParse(parts[4], out int terminalNumber))
                {
                    _logger.LogWarning("Invalid terminal ID in data: {Data}", terminalDataString);
                    continue;
                }

                string terminalId = $"{_config.TerminalIdPrefix}{terminalNumber}";
                string statusKey = string.Format(TerminalStatusKeyPattern, terminalId);

                // Create terminal info for cache - no need to store in Redis
                var terminalInfo = CreateTerminalInfoFromString(terminalId, terminalDataString);
                // Add to in-memory cache first
                if (_terminalInfoCache.TryAdd(terminalId, terminalInfo))
                {
                    _logger.LogDebug("Terminal info added to cache: {TerminalId}", terminalId);
                }
                // Check if this terminal already exists in Redis
                bool exists = await _db.KeyExistsAsync(statusKey);
                if (!exists)
                {
                    // Set initial status in Redis (only dynamic data)
                    var initialStatus = new TerminalStatus
                    {
                        TerminalId = terminalId,
                        Status = TerminalStatusConstants.Available,
                        PodName = string.Empty,
                        LastUsedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };

                    await UpdateTerminalStatusAsync(initialStatus);

                    // Add to available queue (list-based)
                    await _db.ListRightPushAsync(TerminalQueueKey, terminalId);
                    _logger.LogInformation("Added terminal to queue: {TerminalId}", terminalId);
                }
                else
                {
                    _logger.LogInformation("Terminal already exists: {TerminalId}", terminalId);

                    // Make sure it's in the cache
                    if (!_terminalInfoCache.ContainsKey(terminalId))
                    {
                        var terminal = CreateTerminalInfoFromString(terminalId, terminalDataString);
                        _terminalInfoCache.TryAdd(terminalId, terminal);
                        _logger.LogDebug("Added existing terminal to cache: {TerminalId}", terminalId);
                    }
                }
            }

            _logger.LogInformation("Terminal initialization completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing terminals in Redis");
            throw;
        }
    }

    public void PreloadTerminalsInfoToMemoryAsync()
    {
        try
        {
            // Get terminals data from configuration
            var terminalsData = _appConfig.GetSection("TerminalsData").Get<string[]>();
            if (terminalsData == null || terminalsData.Length == 0)
            {
                _logger.LogWarning("No terminals data found in configuration. Skipping initialization.");
                return;
            }

            _logger.LogInformation("Found {Count} terminals in configuration", terminalsData.Length);

            foreach (var terminalDataString in terminalsData)
            {
                // Parse terminal data string
                // Format: Address|Port|Username|Password|TerminalId|Branch
                var parts = terminalDataString.Split('|');
                if (parts.Length < 6)
                {
                    _logger.LogWarning("Invalid terminal data format for entry: {Data}", terminalDataString);
                    continue;
                }

                // Extract terminal ID from the data string (5th element)
                if (!int.TryParse(parts[4], out int terminalNumber))
                {
                    _logger.LogWarning("Invalid terminal ID in data: {Data}", terminalDataString);
                    continue;
                }

                string terminalId = $"{_config.TerminalIdPrefix}{terminalNumber}";

                // Create terminal info for cache - no need to store in Redis
                var terminalInfo = CreateTerminalInfoFromString(terminalId, terminalDataString);
                // Add to in-memory cache first
                if (_terminalInfoCache.TryAdd(terminalId, terminalInfo))
                {
                    _logger.LogDebug("Terminal info added to cache: {TerminalId}", terminalId);
                }
            }

            _logger.LogInformation("Terminal preload completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preloading terminals in memory");
            throw;
        }
    }

    public async Task<bool> IsInitialized()
    {
        // Get terminals data from configuration
        var terminalsData = _appConfig.GetSection("TerminalsData").Get<string[]>();
        if (terminalsData == null || terminalsData.Length == 0)
        {
            _logger.LogWarning("No terminals data found in configuration. Cannot determine initialization state.");
            return false;
        }

        // Count the number of terminal status keys in Redis (async)
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var statusKeys = server.KeysAsync(pattern: string.Format(TerminalStatusKeyPattern, "*"));
        int statusCount = 0;
        await foreach (var key in statusKeys)
        {
            statusCount++;
        }

        _logger.LogInformation("IsInitialized check: {StatusCount} status keys, {TerminalsCount} terminals in config", statusCount, terminalsData.Length);
        return statusCount > 0 && statusCount == terminalsData.Length;
    }

    /// <summary>
    /// Allocate a terminal from the pool
    /// </summary>
    public async Task<TerminalInfo?> AllocateTerminalAsync(int waitTimeoutSeconds)
    {
        _logger.LogInformation("Attempting to allocate a terminal with timeout: {Timeout}s", waitTimeoutSeconds);
        try
        {
            // Use native BLPOP via ExecuteAsync. This method blocks until a terminal is available or the timeout expires.
            var result = await _db.ExecuteAsync("BLPOP", TerminalQueueKey, waitTimeoutSeconds.ToString());
            if (result.IsNull)
            {
                _logger.LogWarning("No terminals available after waiting {Timeout} seconds", waitTimeoutSeconds);
                return null;
            }
            try
            {
                if (result.Resp2Type != ResultType.Array)
                {
                    _logger.LogError("Unexpected BLPOP result format: {Result}", result);
                    return null;
                }
                var terminalId = (string?)result[1];
                if (!string.IsNullOrEmpty(terminalId))
                {
                    _logger.LogInformation("Allocated terminal: {TerminalId}", terminalId);
                    var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var status = new TerminalStatus
                    {
                        TerminalId = terminalId,
                        Status = TerminalStatusConstants.InUse,
                        PodName = _podId,
                        LastUsedTime = currentTime
                    };
                    await UpdateTerminalStatusAsync(status);
                    var terminalInfo = GetTerminalInfo(terminalId);
                    if (terminalInfo != null)
                    {
                        return terminalInfo;
                    }
                    else
                    {
                        _logger.LogWarning("Terminal info not found in cache for allocated terminal: {TerminalId}", terminalId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing allocated terminal result: {Result}", result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error allocating terminal from Redis");
            throw;
        }

        return null;
    }

    /// <summary>
    /// Release a terminal back to the pool
    /// </summary>
    public async Task ReleaseTerminalAsync(string terminalId)
    {
        if (string.IsNullOrEmpty(terminalId))
        {
            _logger.LogWarning("ReleaseTerminalAsync called with null or empty terminalId");
            return;
        }
        _logger.LogInformation("Releasing terminal: {TerminalId}", terminalId);
        try
        {
            var status = new TerminalStatus
            {
                TerminalId = terminalId,
                Status = TerminalStatusConstants.Available,
                PodName = string.Empty,
                LastUsedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            await UpdateTerminalStatusAsync(status);
            // Use _releaseDb for queue push
            await _releaseDb.ListRightPushAsync(TerminalQueueKey, terminalId);
            _logger.LogInformation("Terminal released and added back to queue: {TerminalId}", terminalId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing terminal: {TerminalId}", terminalId);
            throw;
        }
    }

    /// <summary>
    /// Get or create a session for a terminal
    /// </summary>
    public async Task<string> GetOrCreateSessionAsync(string terminalId)
    {
        _logger.LogInformation("GetOrCreateSessionAsync called for terminal: {TerminalId}", terminalId);
        string sessionKey = string.Format(TerminalSessionKeyPattern, terminalId);
        string? sessionId = await _db.StringGetAsync(sessionKey);
        if (!string.IsNullOrEmpty(sessionId))
        {
            // Session exists, refresh TTL
            await _db.KeyExpireAsync(sessionKey, TimeSpan.FromSeconds(_config.SessionTimeoutSeconds));
            return sessionId;
        }
        // TODO: Create new session. This needs to be implemented by the real application logic.
        sessionId = $"session-{terminalId}-{Guid.NewGuid()}";
        await _db.StringSetAsync(sessionKey, sessionId, TimeSpan.FromSeconds(_config.SessionTimeoutSeconds));
        return sessionId;
    }

    /// <summary>
    /// Update status for a terminal (public for interface compliance)
    /// </summary>
    private async Task UpdateTerminalStatusAsync(TerminalStatus status)
    {
        string statusKey = string.Format(TerminalStatusKeyPattern, status.TerminalId);
        var hashEntries = new HashEntry[]
        {
            new HashEntry(nameof(TerminalStatus.TerminalId), status.TerminalId),
            new HashEntry(nameof(TerminalStatus.Status), status.Status),
            new HashEntry(nameof(TerminalStatus.PodName), status.PodName ?? string.Empty),
            new HashEntry(nameof(TerminalStatus.LastUsedTime), status.LastUsedTime)
        };
        await _db.HashSetAsync(statusKey, hashEntries);
        await _db.KeyExpireAsync(statusKey, TimeSpan.FromMinutes(30));
        _logger.LogDebug("Updated terminal status in Redis (hash): {TerminalId} - {Status}", status.TerminalId, status.Status);
    }

    /// <summary>
    /// Get the information of all terminals
    /// </summary>
    public async Task<List<TerminalStatus>> GetTerminalStatusListAsync()
    {
        _logger.LogInformation("GetTerminalStatusListAsync called");
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var statusKeys = server.Keys(pattern: string.Format(TerminalStatusKeyPattern, "*"));
        var terminalStatuses = new List<TerminalStatus>();
        foreach (var key in statusKeys)
        {
            var hash = await _db.HashGetAllAsync(key);
            if (hash.Length > 0)
            {
                var status = new TerminalStatus
                {
                    TerminalId = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.TerminalId)).Value!,
                    Status = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.Status)).Value!,
                    PodName = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.PodName)).Value!,
                    LastUsedTime = (long)hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.LastUsedTime)).Value
                };
                terminalStatuses.Add(status);
            }
        }
        _logger.LogInformation("Retrieved {Count} terminal statuses from Redis", terminalStatuses.Count);
        return terminalStatuses;
    }

    /// <summary>
    /// Reclaim orphaned terminals
    /// </summary>
    public async Task ReclaimOrphanedTerminalsAsync()
    {
        _logger.LogInformation("ReclaimOrphanedTerminalsAsync called");
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var statusKeys = server.Keys(pattern: string.Format(TerminalStatusKeyPattern, "*"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var reclaimed = 0;
        foreach (var key in statusKeys)
        {
            var hash = await _db.HashGetAllAsync(key);
            if (hash.Length == 0) continue;
            var status = new TerminalStatus
            {
                TerminalId = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.TerminalId)).Value!,
                Status = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.Status)).Value!,
                PodName = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.PodName)).Value!,
                LastUsedTime = (long)hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.LastUsedTime)).Value
            };
            if (status.Status == TerminalStatusConstants.InUse && now - status.LastUsedTime > 60 * 60)
            {
                _logger.LogWarning("Reclaiming orphaned terminal: {TerminalId}", status.TerminalId);
                status.Status = TerminalStatusConstants.Available;
                status.PodName = string.Empty;
                status.LastUsedTime = now;
                await UpdateTerminalStatusAsync(status);
                await _db.ListRightPushAsync(TerminalQueueKey, status.TerminalId);
                reclaimed++;
            }
        }
        _logger.LogInformation("Reclaimed {Count} orphaned terminals", reclaimed);
    }

    /// <summary>
    /// Shutdown - release all terminals allocated by this pod
    /// </summary>
    public async Task ShutdownAsync()
    {
        _logger.LogInformation("ShutdownAsync called");
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var statusKeys = server.Keys(pattern: string.Format(TerminalStatusKeyPattern, "*"));
        var released = 0;
        foreach (var key in statusKeys)
        {
            var hash = await _db.HashGetAllAsync(key);
            if (hash.Length == 0) continue;
            var status = new TerminalStatus
            {
                TerminalId = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.TerminalId)).Value!,
                Status = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.Status)).Value!,
                PodName = hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.PodName)).Value!,
                LastUsedTime = (long)hash.FirstOrDefault(e => e.Name == nameof(TerminalStatus.LastUsedTime)).Value
            };
            if (status.Status == TerminalStatusConstants.InUse && status.PodName == _podId)
            {
                _logger.LogInformation("Releasing terminal {TerminalId} held by this pod", status.TerminalId);
                status.Status = TerminalStatusConstants.Available;
                status.PodName = string.Empty;
                status.LastUsedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await UpdateTerminalStatusAsync(status);
                await _db.ListRightPushAsync(TerminalQueueKey, status.TerminalId);
                released++;
            }
        }
        _logger.LogInformation("Shutdown released {Count} terminals held by this pod", released);
    }

    /// <summary>
    /// Get terminal information from cache or Redis
    /// </summary>
    private TerminalInfo? GetTerminalInfo(string terminalId)
    {
        return _terminalInfoCache.GetValueOrDefault(terminalId);
    }

    /// <summary>
    /// Create TerminalInfo object from terminal data string
    /// </summary>
    private TerminalInfo CreateTerminalInfoFromString(string terminalId, string terminalDataString)
    {
        var parts = terminalDataString.Split('|');
        return new TerminalInfo
        {
            Id = terminalId,
            Address = parts[0],
            Port = int.Parse(parts[1]),
            Username = parts[2],
            Password = parts[3],
            Branch = int.TryParse(parts[5], out var branch) ? branch : 0
            // Set other fields as needed
        };
    }
}
