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

    Task<bool> IsInitialized();

    /// <summary>
    /// Allocate a terminal from the queue
    /// </summary>
    Task<TerminalInfo?> AllocateTerminalAsync(int waitTimeoutSeconds = 15);

    /// <summary>
    /// Release a terminal back to the queue
    /// </summary>
    Task ReleaseTerminalAsync(string terminalId);

    /// <summary>
    /// Get or create a session for a terminal
    /// </summary>
    Task<string> GetOrCreateSessionAsync(string terminalId);

    /// <summary>
    /// Refresh the session timeout (TTL, time to live) for a terminal
    /// </summary>
    Task RefreshSessionTtlAsync(string terminalId);

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
    /// Get the information of all terminals
    /// </summary>
    Task<List<TerminalStatus>> GetTerminalStatusListAsync();

    ///// <summary>
    ///// Update status for a terminal
    ///// </summary>
    //Task UpdateTerminalStatusAsync(TerminalStatus status);
}
