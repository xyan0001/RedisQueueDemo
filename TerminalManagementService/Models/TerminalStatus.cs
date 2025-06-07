namespace TerminalManagementService.Models;

/// <summary>
/// Represents the dynamic status information of a terminal
/// </summary>
public class TerminalStatus
{
    /// <summary>
    /// The terminal's unique identifier
    /// </summary>
    public required string TerminalId { get; set; }

    /// <summary>
    /// Current status of the terminal (available or in_use)
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Name of the pod currently using this terminal (if in use)
    /// </summary>
    public required string PodName { get; set; }

    /// <summary>
    /// Unix timestamp of last activity
    /// </summary>
    public long LastUsedTime { get; set; }
}

/// <summary>
/// Status constants for terminals
/// </summary>
public static class TerminalStatusConstants
{
    public const string Available = "available";
    public const string InUse = "in_use";
}
