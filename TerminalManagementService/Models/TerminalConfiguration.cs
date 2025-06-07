using System.Collections.Generic;

namespace TerminalManagementService.Models;    /// <summary>
    /// Configuration for terminal details
    /// </summary>
    public class TerminalConfiguration
    {
        /// <summary>
        /// Common URL for all terminals
        /// </summary>
        public required string Url { get; set; }

        /// <summary>
        /// Common port for all terminals
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Base username pattern (e.g., "user{0}" where {0} is the terminal ID)
        /// </summary>
        public required string UsernamePattern { get; set; }

        /// <summary>
        /// Base password pattern (e.g., "pass{0}" where {0} is the terminal ID)
        /// </summary>
        public required string PasswordPattern { get; set; }

        /// <summary>
        /// Terminal ID prefix
        /// </summary>
        public required string TerminalIdPrefix { get; set; }

        /// <summary>
        /// Initial number of terminals to create
        /// </summary>
        public int InitialTerminalCount { get; set; }

        /// <summary>
        /// Session timeout in seconds (default 300 seconds = 5 minutes)
        /// </summary>
        public int SessionTimeoutSeconds { get; set; } = 300;        /// <summary>
        /// Orphaned terminal timeout in seconds (default 30 seconds)
        /// </summary>
        public int OrphanedTerminalTimeoutSeconds { get; set; } = 30;
    }
