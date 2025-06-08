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
    /// Preload terminal information into memory
    /// </summary>
    /// <returns></returns>
    void PreloadTerminalsInfoToMemoryAsync();

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
    /// Get or create a session for a terminal. Also refreshes the TTL for the session.
    /// </summary>
    Task<string> GetOrCreateSessionAsync(string terminalId);

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
