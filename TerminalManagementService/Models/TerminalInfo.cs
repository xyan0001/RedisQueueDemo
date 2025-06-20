namespace TerminalManagementService.Models;

/// <summary>
/// Represents the static information about a terminal
/// </summary>
public class TerminalInfo
{
    /// <summary>
    /// Unique identifier for the terminal
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Address to connect to the terminal
    /// </summary>
    public required string Address { get; set; }

    /// <summary>
    /// Port to connect to the terminal
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Username for authentication
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// Password for authentication
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    /// Branch identifier for the terminal
    /// </summary>
    public required int Branch { get; set; }
}
