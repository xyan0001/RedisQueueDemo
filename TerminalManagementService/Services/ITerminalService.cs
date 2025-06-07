using System.Threading.Tasks;
using TerminalManagementService.Models;

namespace TerminalManagementService.Services;

/// <summary>
/// Interface for terminal management service
/// </summary>
public interface ITerminalService
{
    /// <summary>
    /// Initialize terminals in Redis based on configuration
    /// </summary>
    Task InitializeTerminalsAsync();

    /// <summary>
    /// Add new terminals to the pool
    /// </summary>
    Task AddTerminalsAsync(int startIndex, int count);

    /// <summary>
    /// Allocate a terminal from the pool
    /// </summary>
    Task<TerminalInfo> AllocateTerminalAsync();

    /// <summary>
    /// Release a terminal back to the pool
    /// </summary>
    Task ReleaseTerminalAsync(string terminalId);

    /// <summary>
    /// Get or create a session for a terminal
    /// </summary>
    Task<string> GetOrCreateSessionAsync(string terminalId);

    /// <summary>
    /// Refresh the session timeout for a terminal
    /// </summary>
    Task RefreshSessionAsync(string terminalId);

    /// <summary>
    /// Update the last used time for a terminal
    /// </summary>
    Task UpdateLastUsedTimeAsync(string terminalId);

    /// <summary>
    /// Reclaim orphaned terminals
    /// </summary>
    Task ReclaimOrphanedTerminalsAsync();
    
    /// <summary>
    /// Shutdown - release all terminals allocated by this pod
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Get cache performance metrics
    /// </summary>
    (long hits, long misses, double hitRate) GetCacheMetrics();
}
