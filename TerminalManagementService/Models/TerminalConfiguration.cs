namespace TerminalManagementService.Models;

/// <summary>
/// Configuration for terminal details
/// </summary>
public class TerminalConfiguration
{
    /// <summary>
    /// Pod name for this instance
    /// </summary>
    public string PodName { get; set; } = "local-pod";

    /// <summary>
    /// Secret to use for terminal passwords
    /// </summary>
    public string Secret { get; set; } = "<terminals_password>";

    /// <summary>
    /// Connection scheme (http, https)
    /// </summary>
    public string Scheme { get; set; } = "http";

    /// <summary>
    /// Base username pattern (e.g., "user{0}" where {0} is the terminal ID)
    /// </summary>
    public required string UsernamePattern { get; set; }

    /// <summary>
    /// Base password pattern (e.g., "pass{0}" where {0} is the terminal ID)
    /// </summary>
    public required string PasswordPattern { get; set; }    /// <summary>
    /// Terminal ID prefix
    /// </summary>
    public required string TerminalIdPrefix { get; set; }
    
    /// <summary>
    /// Initial number of terminals to create
    /// </summary>
    public int InitialTerminalCount { get; set; } = 40;

    /// <summary>
    /// Session timeout in seconds (default 300 seconds = 5 minutes)
    /// </summary>
    public int SessionTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Orphaned terminal timeout in seconds (default 30 seconds)
    /// </summary>
    public int OrphanedTerminalTimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Environment type for the service (Kubernetes, Local, etc.)
    /// </summary>
    public string Environment { get; set; } = "Local";
    
    /// <summary>
    /// When true, the service will initialize Redis on startup
    /// Set to false when using a dedicated initialization job in Kubernetes
    /// </summary>
    public bool InitializeRedisOnStartup { get; set; } = true;
}
