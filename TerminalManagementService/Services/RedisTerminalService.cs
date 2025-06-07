using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using TerminalManagementService.Models;

namespace TerminalManagementService.Services;

public class RedisTerminalService : ITerminalService
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisTerminalService> _logger;
    private readonly TerminalConfiguration _config;
    private readonly IConfiguration _appConfig;
    private readonly string _podId;
    private readonly ConcurrentDictionary<string, TerminalInfo> _terminalInfoCache;

    // Redis key patterns - we no longer need TerminalInfoKeyPattern as we store config in appsettings
    private const string TerminalStatusKeyPattern = "terminal:status:{0}";
    private const string TerminalSessionKeyPattern = "terminal:session:{0}";
    private const string TerminalPoolKey = "terminal_pool";

    // Cache metrics
    private long _cacheHits = 0;
    private long _cacheMisses = 0;

    public RedisTerminalService(
        ConnectionMultiplexer redis,
        IOptions<TerminalConfiguration> config,
        IConfiguration appConfig,
        ILogger<RedisTerminalService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _db = _redis.GetDatabase();
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
        // Skip initialization if not configured to initialize Redis on startup
        if (!_config.InitializeRedisOnStartup)
        {
            _logger.LogInformation("Skipping Redis initialization as per configuration settings");
            await PreloadTerminalCacheAsync();
            return;
        }

        _logger.LogInformation("Initializing terminals in Redis...");

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

                // Check if this terminal already exists
                bool exists = await _db.KeyExistsAsync(statusKey);
                if (!exists)
                {
                    // Create terminal info for cache - no need to store in Redis
                    var terminal = CreateTerminalInfoFromString(terminalId, terminalDataString);

                    // Add to cache
                    _terminalInfoCache.TryAdd(terminalId, terminal);
                    _logger.LogDebug("Terminal info added to cache: {TerminalId}", terminalId);

                    // Set initial status in Redis (only dynamic data)
                    var initialStatus = new TerminalStatus
                    {
                        TerminalId = terminalId,
                        Status = TerminalStatusConstants.Available,
                        PodName = string.Empty,
                        LastUsedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };

                    await UpdateTerminalStatusAsync(initialStatus);

                    // Add to available pool
                    await _db.SetAddAsync(TerminalPoolKey, terminalId);
                    _logger.LogInformation("Added terminal to pool: {TerminalId}", terminalId);
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

    /// <summary>
    /// Add new terminals to the pool
    /// </summary>
    public async Task AddTerminalsAsync(int startIndex, int count)
    {
        _logger.LogInformation("Adding {Count} new terminals starting at index {StartIndex}", count, startIndex);

        try
        {
            // Get terminals data from configuration
            var terminalsData = _appConfig.GetSection("TerminalsData").Get<string[]>();
            if (terminalsData == null || terminalsData.Length == 0)
            {
                _logger.LogWarning("No terminals data found in configuration. Skipping adding terminals.");
                return;
            }

            // Ensure we don't exceed the available data
            int maxIndex = Math.Min(startIndex + count - 1, terminalsData.Length);
            for (int i = startIndex; i <= maxIndex; i++)
            {
                // Get the terminal data entry (adjusting for 0-based array)
                string terminalDataEntry = terminalsData[i - 1];
                var parts = terminalDataEntry.Split('|');

                if (parts.Length < 6)
                {
                    _logger.LogWarning("Invalid terminal data format: {Data}", terminalDataEntry);
                    continue;
                }

                // Extract terminal ID from the data string (5th element)
                if (!int.TryParse(parts[4], out int terminalNumber))
                {
                    _logger.LogWarning("Invalid terminal ID in data: {Data}", terminalDataEntry);
                    continue;
                }

                string terminalId = $"{_config.TerminalIdPrefix}{terminalNumber}";
                string statusKey = string.Format(TerminalStatusKeyPattern, terminalId);

                // Check if this terminal already exists
                bool exists = await _db.KeyExistsAsync(statusKey);
                if (!exists)
                {
                    // Create terminal info for cache only - no need to store in Redis
                    var terminal = CreateTerminalInfoFromString(terminalId, terminalDataEntry);

                    // Add to cache
                    _terminalInfoCache.TryAdd(terminalId, terminal);
                    _logger.LogDebug("New terminal info added to cache: {TerminalId}", terminalId);

                    // Set initial status in Redis (only dynamic data)
                    var initialStatus = new TerminalStatus
                    {
                        TerminalId = terminalId,
                        Status = TerminalStatusConstants.Available,
                        PodName = string.Empty,
                        LastUsedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };

                    await UpdateTerminalStatusAsync(initialStatus);

                    // Add to available pool
                    await _db.SetAddAsync(TerminalPoolKey, terminalId);
                    _logger.LogInformation("Added new terminal to pool: {TerminalId}", terminalId);
                }
                else
                {
                    _logger.LogInformation("Terminal already exists: {TerminalId}", terminalId);
                }
            }

            _logger.LogInformation("New terminals added successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding new terminals to Redis");
            throw;
        }
    }

    /// <summary>
    /// Allocate a terminal from the pool
    /// </summary>
    public async Task<TerminalInfo> AllocateTerminalAsync()
    {
        _logger.LogInformation("Attempting to allocate a terminal");
        try
        {
            // Atomically pop a terminal from the available pool
            var terminalId = await _db.SetPopAsync(TerminalPoolKey);
            if (terminalId.IsNull)
            {
                _logger.LogWarning("No terminals available in the pool");
                return null!; // Using null-forgiving operator as we properly check for null in calling code
            }

            string id = terminalId.ToString();
            _logger.LogInformation("Allocated terminal: {TerminalId}", id);

            // Update terminal status
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var status = new TerminalStatus
            {
                TerminalId = id,
                Status = TerminalStatusConstants.InUse,
                PodName = _podId,
                LastUsedTime = currentTime
            };

            await UpdateTerminalStatusAsync(status);            // Get terminal info from cache or configuration
            var terminalInfo = GetTerminalInfo(id);
            if (terminalInfo == null)
            {
                _logger.LogError("Failed to get terminal info for allocated terminal: {TerminalId}", id);
                // Note: We do NOT return the terminal to the pool here since it has a configuration issue
                // This terminal will remain in 'in_use' state and will be reclaimed by the orphaned terminal process
                return null!; // Using null-forgiving operator as we properly check for null in calling code
            }

            return terminalInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error allocating terminal");
            throw;
        }
    }

    /// <summary>
    /// Release a terminal back to the pool
    /// </summary>
    public async Task ReleaseTerminalAsync(string terminalId)
    {
        _logger.LogInformation("Releasing terminal: {TerminalId}", terminalId);

        try
        {
            // Update status to available
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var status = new TerminalStatus
            {
                TerminalId = terminalId,
                Status = TerminalStatusConstants.Available,
                PodName = string.Empty,
                LastUsedTime = currentTime
            };

            await UpdateTerminalStatusAsync(status);

            // Add back to available pool
            await _db.SetAddAsync(TerminalPoolKey, terminalId);

            _logger.LogInformation("Terminal released: {TerminalId}", terminalId);
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
        if (string.IsNullOrEmpty(terminalId))
        {
            throw new ArgumentNullException(nameof(terminalId));
        }

        string sessionKey = string.Format(TerminalSessionKeyPattern, terminalId);

        try
        {
            // Try to get existing session
            var sessionId = await _db.StringGetAsync(sessionKey);
            if (!sessionId.IsNull)
            {
                // Refresh session TTL
                await _db.KeyExpireAsync(sessionKey, TimeSpan.FromSeconds(_config.SessionTimeoutSeconds));
                _logger.LogInformation("Reusing existing session for terminal: {TerminalId}", terminalId);
                return sessionId.ToString();
            } // No existing session, create new one

            _logger.LogInformation("Creating new session for terminal: {TerminalId}", terminalId);

            // Get terminal info from cache or configuration
            var terminalInfo = GetTerminalInfo(terminalId);
            if (terminalInfo == null)
            {
                _logger.LogError("Failed to get terminal info for session creation: {TerminalId}", terminalId);
                throw new InvalidOperationException($"Terminal info not found for terminal: {terminalId}");
            }

            // Login to terminal and get session ID (implementation depends on terminal system)
            string newSessionId = await LoginToTerminalAsync(terminalInfo);

            // Store session with expiration
            await _db.StringSetAsync(sessionKey, newSessionId, TimeSpan.FromSeconds(_config.SessionTimeoutSeconds));

            return newSessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error managing session for terminal: {TerminalId}", terminalId);
            throw;
        }
    }

    /// <summary>
    /// Refresh the session timeout for a terminal
    /// </summary>
    public async Task RefreshSessionAsync(string terminalId)
    {
        string sessionKey = string.Format(TerminalSessionKeyPattern, terminalId);

        try
        {
            // Check if session exists
            bool exists = await _db.KeyExistsAsync(sessionKey);
            if (exists)
            {
                // Refresh expiration
                await _db.KeyExpireAsync(sessionKey, TimeSpan.FromSeconds(_config.SessionTimeoutSeconds));
                _logger.LogDebug("Refreshed session for terminal: {TerminalId}", terminalId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing session for terminal: {TerminalId}", terminalId);
            throw;
        }
    }

    /// <summary>
    /// Update the last used time for a terminal
    /// </summary>
    public async Task UpdateLastUsedTimeAsync(string terminalId)
    {
        try
        {
            var status = await GetTerminalStatusAsync(terminalId);
            status.LastUsedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await UpdateTerminalStatusAsync(status);
            _logger.LogDebug("Updated last_used_time for terminal: {TerminalId}", terminalId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last_used_time for terminal: {TerminalId}", terminalId);
            throw;
        }
    }

    /// <summary>
    /// Reclaim orphaned terminals
    /// </summary>
    public async Task ReclaimOrphanedTerminalsAsync()
    {
        _logger.LogInformation("Checking for orphaned terminals");

        try
        {
            // Get all terminal IDs (would use SCAN in production for large datasets)
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: "terminal:status:*").ToArray();

            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timeoutThreshold = currentTime - _config.OrphanedTerminalTimeoutSeconds;

            foreach (var key in keys)
            {
                string terminalId = key.ToString().Replace("terminal:status:", "");
                var terminalStatus = await GetTerminalStatusAsync(terminalId);

                if (terminalStatus.Status == TerminalStatusConstants.InUse &&
                    terminalStatus.LastUsedTime < timeoutThreshold)
                {
                    _logger.LogWarning("Reclaiming orphaned terminal: {TerminalId}", terminalId);

                    // Update status
                    terminalStatus.Status = TerminalStatusConstants.Available;
                    terminalStatus.PodName = string.Empty;
                    terminalStatus.LastUsedTime = currentTime;

                    await UpdateTerminalStatusAsync(terminalStatus);

                    // Add back to available pool
                    await _db.SetAddAsync(TerminalPoolKey, terminalId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reclaiming orphaned terminals");
            throw;
        }
    }

    /// <summary>
    /// Shutdown - release all terminals allocated by this pod
    /// </summary>
    public async Task ShutdownAsync()
    {
        _logger.LogInformation("Pod shutdown initiated, releasing all terminals");

        try
        {
            // Get all terminal statuses
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: "terminal:status:*").ToArray();

            foreach (var key in keys)
            {
                string terminalId = key.ToString().Replace("terminal:status:", "");
                var terminalStatus = await GetTerminalStatusAsync(terminalId);

                // Check if this terminal is allocated to this pod
                if (terminalStatus.Status == TerminalStatusConstants.InUse &&
                    terminalStatus.PodName == _podId)
                {
                    _logger.LogInformation("Releasing terminal on shutdown: {TerminalId}", terminalId);

                    // Release terminal
                    await ReleaseTerminalAsync(terminalId);
                }
            }

            _logger.LogInformation("All terminals released during shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shutdown terminal release");
            // Don't throw here to allow shutdown to continue
        }
    }

    /// <summary>
    /// Login to a terminal and get session ID
    /// This method would be implemented based on the specific terminal system
    /// </summary>
    private async Task<string> LoginToTerminalAsync(TerminalInfo terminal)
    {
        if (terminal == null)
        {
            throw new ArgumentNullException(nameof(terminal));
        }

        // This is a placeholder for the actual terminal login implementation
        // In a real system, this would connect to the terminal using the provided credentials

        if (terminal.Id == null)
        {
            _logger.LogError("Terminal ID is null");
            throw new InvalidOperationException("Terminal ID is null");
        }

        _logger.LogInformation("Logging in to terminal: {TerminalId}", terminal.Id);

        // Simulate login by generating a session ID
        // In a real system, you would connect to the terminal and perform authentication
        string sessionId = $"session-{Guid.NewGuid()}";

        // Simulate network delay
        await Task.Delay(50);

        _logger.LogInformation("Login successful for terminal: {TerminalId}", terminal.Id);

        return sessionId;
    }

    /// <summary>
    /// Create a terminal info object from a data string
    /// Format: Address|Port|Username|Password|TerminalId|Branch
    /// </summary>
    private TerminalInfo CreateTerminalInfoFromString(string terminalId, string dataString)
    {
        var parts = dataString.Split('|');
        if (parts.Length < 6)
        {
            throw new ArgumentException($"Invalid terminal data format: {dataString}", nameof(dataString));
        }

        return new TerminalInfo
        {
            Id = terminalId,
            Address = parts[0],
            Port = int.Parse(parts[1]),
            Username = parts[2],
            Password = _config.Secret, // Use the Secret from config instead of the one in the data string
            Branch = int.Parse(parts[5])
        };
    }

    /// <summary>
    /// Get terminal info from cache or create from configuration
    /// </summary>
    private TerminalInfo GetTerminalInfo(string terminalId)
    {
        if (string.IsNullOrEmpty(terminalId))
        {
            throw new ArgumentNullException(nameof(terminalId));
        }

        // Try to get from cache first
        if (_terminalInfoCache.TryGetValue(terminalId, out var cachedInfo))
        {
            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug("Terminal info retrieved from cache: {TerminalId}", terminalId);
            return cachedInfo;
        }

        // Not in cache, find in terminals data
        Interlocked.Increment(ref _cacheMisses);
        _logger.LogDebug("Terminal info not in cache, looking in terminals data: {TerminalId}", terminalId);

        // Extract terminal number from terminalId
        if (terminalId.StartsWith(_config.TerminalIdPrefix) &&
            int.TryParse(terminalId.Substring(_config.TerminalIdPrefix.Length), out int terminalId_value))
        {
            // Get terminals data from configuration
            var terminalsData = _appConfig.GetSection("TerminalsData").Get<string[]>();
            if (terminalsData != null)
            {
                // Search for terminal data with matching terminal ID
                foreach (var terminalDataString in terminalsData)
                {
                    var parts = terminalDataString.Split('|');
                    if (parts.Length >= 6 && int.TryParse(parts[4], out int currentId) && currentId == terminalId_value)
                    {
                        var terminalInfo = CreateTerminalInfoFromString(terminalId, terminalDataString);

                        // Add to cache
                        _terminalInfoCache.TryAdd(terminalId, terminalInfo);
                        _logger.LogDebug("Terminal info created and added to cache: {TerminalId}", terminalId);

                        return terminalInfo;
                    }
                }
            }
        }

        _logger.LogWarning("Could not create terminal info for invalid terminal ID: {TerminalId}", terminalId);
        return null!; // Using null-forgiving operator as we properly check for null in calling code
    }

    /// <summary>
    /// Preload all terminal information into cache
    /// </summary>
    public async Task PreloadTerminalCacheAsync()
    {
        _logger.LogInformation("Preloading terminal info cache...");

        try
        {
            // Get all terminals from the pool
            var terminalIds = await _db.SetMembersAsync(TerminalPoolKey);

            // Get terminals data from configuration
            var terminalsData = _appConfig.GetSection("TerminalsData").Get<string[]>();
            if (terminalsData == null || terminalsData.Length == 0)
            {
                _logger.LogWarning("No terminals data found in configuration. Skipping preloading cache.");
                return;
            }

            // Dictionary to map terminal IDs to their data
            var terminalDataById = new Dictionary<string, string>();
            foreach (var terminalDataString in terminalsData)
            {
                var parts = terminalDataString.Split('|');
                if (parts.Length >= 6 && int.TryParse(parts[4], out int terminalNumber))
                {
                    string formattedId = $"{_config.TerminalIdPrefix}{terminalNumber}";
                    terminalDataById[formattedId] = terminalDataString;
                }
            }

            // Process terminals in the pool
            foreach (var terminalId in terminalIds)
            {
                string id = terminalId.ToString();
                if (terminalDataById.TryGetValue(id, out var terminalDataString))
                {
                    var terminalInfo = CreateTerminalInfoFromString(id, terminalDataString);
                    _terminalInfoCache.TryAdd(id, terminalInfo);
                }
            }

            // Process terminals that are in use (not in the pool)
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var statusKeys = server.Keys(pattern: "terminal:status:*")
                .Select(k => k.ToString().Replace("terminal:status:", ""));

            foreach (var id in statusKeys)
            {
                if (!terminalIds.Contains(id) && terminalDataById.TryGetValue(id, out var terminalDataString))
                {
                    var terminalInfo = CreateTerminalInfoFromString(id, terminalDataString);
                    _terminalInfoCache.TryAdd(id, terminalInfo);
                }
            }

            _logger.LogInformation("Terminal info cache preloaded with {Count} terminals", _terminalInfoCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preloading terminal info cache");
        }
    }

    /// <summary>
    /// Get status information for a terminal
    /// </summary>
    public async Task<TerminalStatus> GetTerminalStatusAsync(string terminalId)
    {
        if (string.IsNullOrEmpty(terminalId))
        {
            throw new ArgumentNullException(nameof(terminalId));
        }

        string statusKey = string.Format(TerminalStatusKeyPattern, terminalId);
        var hashEntries = await _db.HashGetAllAsync(statusKey);

        if (hashEntries.Length == 0)
        {
            return new TerminalStatus
            {
                TerminalId = terminalId,
                Status = TerminalStatusConstants.Available,
                PodName = string.Empty,
                LastUsedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }

        // Get values with explicit type information to avoid null reference warnings
        string status = GetHashValueString(hashEntries, "status", TerminalStatusConstants.Available);
        string podName = GetHashValueString(hashEntries, "pod_name", string.Empty);
        long lastUsedTime = GetHashValueLong(hashEntries, "last_used_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        return new TerminalStatus
        {
            TerminalId = terminalId,
            Status = status,
            PodName = podName,
            LastUsedTime = lastUsedTime
        };
    }

    /// <summary>
    /// Update status for a terminal
    /// </summary>
    public async Task UpdateTerminalStatusAsync(TerminalStatus status)
    {
        if (status == null)
        {
            throw new ArgumentNullException(nameof(status));
        }

        string statusKey = string.Format(TerminalStatusKeyPattern, status.TerminalId);
        var statusHashEntries = new HashEntry[]
        {
            new HashEntry("status", status.Status),
            new HashEntry("pod_name", status.PodName),
            new HashEntry("last_used_time", status.LastUsedTime)
        };

        await _db.HashSetAsync(statusKey, statusHashEntries);
        _logger.LogDebug("Updated terminal status for {TerminalId}: {Status}", status.TerminalId, status.Status);
    }

    /// <summary>
    /// Get cache performance metrics
    /// </summary>
    public (long hits, long misses, double hitRate) GetCacheMetrics()
    {
        long hits = _cacheHits;
        long misses = _cacheMisses;
        long total = hits + misses;
        double hitRate = total > 0 ? (double)hits / total * 100 : 0;

        return (hits, misses, hitRate);
    }

    /// <summary>
    /// Get string value from HashEntry array safely
    /// </summary>
    private string GetHashValueString(HashEntry[] entries, string name, string defaultValue)
    {
        if (entries == null || string.IsNullOrEmpty(name))
        {
            return defaultValue;
        }

        var entry = entries.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.Ordinal));
        if (entry.Equals(default(HashEntry)) || entry.Value.IsNull)
        {
            return defaultValue;
        }

        try
        {
            return entry.Value.ToString() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Get long value from HashEntry array safely
    /// </summary>
    private long GetHashValueLong(HashEntry[] entries, string name, long defaultValue)
    {
        if (entries == null || string.IsNullOrEmpty(name))
        {
            return defaultValue;
        }

        var entry = entries.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.Ordinal));
        if (entry.Equals(default(HashEntry)) || entry.Value.IsNull)
        {
            return defaultValue;
        }

        try
        {
            return (long)entry.Value;
        }
        catch
        {
            return defaultValue;
        }
    }
}
