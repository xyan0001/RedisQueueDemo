using System;

namespace TerminalManagementService.Models
{
    /// <summary>
    /// Represents the static information about a terminal
    /// </summary>
    public class TerminalInfo
    {
        /// <summary>
        /// Unique identifier for the terminal
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// URL to connect to the terminal
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Port to connect to the terminal
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Username for authentication
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Password for authentication
        /// </summary>
        public string Password { get; set; }
    }
}
