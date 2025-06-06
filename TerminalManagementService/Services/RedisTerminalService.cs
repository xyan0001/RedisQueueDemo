using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TerminalManagementService.Models;

namespace TerminalManagementService.Services
{
    public class RedisTerminalService : ITerminalService
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ILogger<RedisTerminalService> _logger;
        private readonly TerminalConfiguration _config;
        private readonly string _podId;

        // Redis key patterns
        private const string TerminalInfoKeyPattern = "terminal:info:{0}";
        private const string TerminalStatusKeyPattern = "terminal:status:{0}";
        private const string TerminalSessionKeyPattern = "terminal:session:{0}";
        private const string TerminalPoolKey = "terminal_pool";

        public RedisTerminalService(
            ConnectionMultiplexer redis,
            IOptions<TerminalConfiguration> config,
            ILogger<RedisTerminalService> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _db = _redis.GetDatabase();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            // Get pod ID from environment variable (set by Kubernetes)
            _podId = Environment.GetEnvironmentVariable("POD_NAME") ?? Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Initialize terminals in Redis based on configuration
        /// </summary>
        public async Task InitializeTerminalsAsync()
        {
            _logger.LogInformation("Initializing terminals in Redis...");

            try
            {
                for (int i = 1; i <= _config.InitialTerminalCount; i++)
                {
                    string terminalId = $"{_config.TerminalIdPrefix}{i:000}";
                    string infoKey = string.Format(TerminalInfoKeyPattern, terminalId);

                    // Check if this terminal already exists
                    bool exists = await _db.KeyExistsAsync(infoKey);
                    if (!exists)
                    {
                        // Create terminal info
                        var terminal = new TerminalInfo
                        {
                            Id = terminalId,
                            Url = _config.Url,
                            Port = _config.Port,
                            Username = string.Format(_config.UsernamePattern, i),
                            Password = string.Format(_config.PasswordPattern, i)
                        };

                        // Store terminal info in Redis
                        var hashEntries = new HashEntry[]
                        {
                            new HashEntry("id", terminalId),
                            new HashEntry("url", terminal.Url),
                            new HashEntry("port", terminal.Port),
                            new HashEntry("username", terminal.Username),
                            new HashEntry("password", terminal.Password)
                        };

                        await _db.HashSetAsync(infoKey, hashEntries);
                        _logger.LogInformation("Created terminal info: {TerminalId}", terminalId);

                        // Set initial status
                        string statusKey = string.Format(TerminalStatusKeyPattern, terminalId);
                        var statusHashEntries = new HashEntry[]
                        {
                            new HashEntry("status", TerminalStatusConstants.Available),
                            new HashEntry("pod_id", string.Empty),
                            new HashEntry("last_used_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                        };
                        await _db.HashSetAsync(statusKey, statusHashEntries);

                        // Add to available pool
                        await _db.SetAddAsync(TerminalPoolKey, terminalId);
                        _logger.LogInformation("Added terminal to pool: {TerminalId}", terminalId);
                    }
                    else
                    {
                        _logger.LogInformation("Terminal already exists: {TerminalId}", terminalId);
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
                for (int i = startIndex; i < startIndex + count; i++)
                {
                    string terminalId = $"{_config.TerminalIdPrefix}{i:000}";
                    string infoKey = string.Format(TerminalInfoKeyPattern, terminalId);

                    // Check if this terminal already exists
                    bool exists = await _db.KeyExistsAsync(infoKey);
                    if (!exists)
                    {
                        // Create terminal info
                        var terminal = new TerminalInfo
                        {
                            Id = terminalId,
                            Url = _config.Url,
                            Port = _config.Port,
                            Username = string.Format(_config.UsernamePattern, i),
                            Password = string.Format(_config.PasswordPattern, i)
                        };

                        // Store terminal info in Redis
                        var hashEntries = new HashEntry[]
                        {
                            new HashEntry("id", terminalId),
                            new HashEntry("url", terminal.Url),
                            new HashEntry("port", terminal.Port),
                            new HashEntry("username", terminal.Username),
                            new HashEntry("password", terminal.Password)
                        };

                        await _db.HashSetAsync(infoKey, hashEntries);
                        _logger.LogInformation("Created new terminal info: {TerminalId}", terminalId);

                        // Set initial status
                        string statusKey = string.Format(TerminalStatusKeyPattern, terminalId);
                        var statusHashEntries = new HashEntry[]
                        {
                            new HashEntry("status", TerminalStatusConstants.Available),
                            new HashEntry("pod_id", string.Empty),
                            new HashEntry("last_used_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                        };
                        await _db.HashSetAsync(statusKey, statusHashEntries);

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
                    return null;
                }

                string id = terminalId.ToString();
                _logger.LogInformation("Allocated terminal: {TerminalId}", id);

                // Update terminal status
                string statusKey = string.Format(TerminalStatusKeyPattern, id);
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                var statusHashEntries = new HashEntry[]
                {
                    new HashEntry("status", TerminalStatusConstants.InUse),
                    new HashEntry("pod_id", _podId),
                    new HashEntry("last_used_time", currentTime)
                };
                
                await _db.HashSetAsync(statusKey, statusHashEntries);

                // Get terminal info
                string infoKey = string.Format(TerminalInfoKeyPattern, id);
                var hashEntries = await _db.HashGetAllAsync(infoKey);
                
                var terminalInfo = new TerminalInfo
                {
                    Id = id,
                    Url = hashEntries.FirstOrDefault(h => h.Name == "url").Value,
                    Port = (int)hashEntries.FirstOrDefault(h => h.Name == "port").Value,
                    Username = hashEntries.FirstOrDefault(h => h.Name == "username").Value,
                    Password = hashEntries.FirstOrDefault(h => h.Name == "password").Value
                };

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
                string statusKey = string.Format(TerminalStatusKeyPattern, terminalId);
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                var statusHashEntries = new HashEntry[]
                {
                    new HashEntry("status", TerminalStatusConstants.Available),
                    new HashEntry("pod_id", string.Empty),
                    new HashEntry("last_used_time", currentTime)
                };
                
                await _db.HashSetAsync(statusKey, statusHashEntries);

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
                    return sessionId;
                }

                // No existing session, create new one
                _logger.LogInformation("Creating new session for terminal: {TerminalId}", terminalId);
                
                // Get terminal info
                string infoKey = string.Format(TerminalInfoKeyPattern, terminalId);
                var hashEntries = await _db.HashGetAllAsync(infoKey);
                
                var terminalInfo = new TerminalInfo
                {
                    Id = terminalId,
                    Url = hashEntries.FirstOrDefault(h => h.Name == "url").Value,
                    Port = (int)hashEntries.FirstOrDefault(h => h.Name == "port").Value,
                    Username = hashEntries.FirstOrDefault(h => h.Name == "username").Value,
                    Password = hashEntries.FirstOrDefault(h => h.Name == "password").Value
                };

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
                string statusKey = string.Format(TerminalStatusKeyPattern, terminalId);
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                await _db.HashSetAsync(statusKey, "last_used_time", currentTime);
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
                    var hashEntries = await _db.HashGetAllAsync(key);
                    
                    string status = hashEntries.FirstOrDefault(h => h.Name == "status").Value;
                    long lastUsed = (long)hashEntries.FirstOrDefault(h => h.Name == "last_used_time").Value;
                    
                    if (status == TerminalStatusConstants.InUse && lastUsed < timeoutThreshold)
                    {
                        _logger.LogWarning("Reclaiming orphaned terminal: {TerminalId}", terminalId);
                        
                        // Update status
                        var statusHashEntries = new HashEntry[]
                        {
                            new HashEntry("status", TerminalStatusConstants.Available),
                            new HashEntry("pod_id", string.Empty),
                            new HashEntry("last_used_time", currentTime)
                        };
                        
                        await _db.HashSetAsync(key, statusHashEntries);
                        
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
                    var hashEntries = await _db.HashGetAllAsync(key);
                    string status = hashEntries.FirstOrDefault(h => h.Name == "status").Value;
                    string podId = hashEntries.FirstOrDefault(h => h.Name == "pod_id").Value;
                    
                    // Check if this terminal is allocated to this pod
                    if (status == TerminalStatusConstants.InUse && podId == _podId)
                    {
                        string terminalId = key.ToString().Replace("terminal:status:", "");
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
            // This is a placeholder for the actual terminal login implementation
            // In a real system, this would connect to the terminal using the provided credentials
            
            _logger.LogInformation("Logging in to terminal: {TerminalId}", terminal.Id);
            
            // Simulate login by generating a session ID
            // In a real system, you would connect to the terminal and perform authentication
            string sessionId = $"session-{Guid.NewGuid()}";
            
            // Simulate network delay
            await Task.Delay(50);
            
            _logger.LogInformation("Login successful for terminal: {TerminalId}", terminal.Id);
            
            return sessionId;
        }
    }
}
