using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TerminalManagementService.Models;
using TerminalManagementService.Services;

namespace TerminalManagementService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TerminalsController : ControllerBase
    {
        private readonly ITerminalService _terminalService;
        private readonly ILogger<TerminalsController> _logger;

        public TerminalsController(
            ITerminalService terminalService,
            ILogger<TerminalsController> logger)
        {
            _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Allocate a terminal from the pool
        /// </summary>
        [HttpPost("allocate")]
        public async Task<IActionResult> AllocateTerminal()
        {
            try
            {
                var terminal = await _terminalService.AllocateTerminalAsync();
                if (terminal == null)
                {
                    return StatusCode(503, "No terminals available");
                }

                // Get session ID
                var sessionId = await _terminalService.GetOrCreateSessionAsync(terminal.Id);

                // Return terminal details with session
                return Ok(new
                {
                    terminal.Id,
                    terminal.Url,
                    terminal.Port,
                    terminal.Username,
                    terminal.Password,
                    SessionId = sessionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error allocating terminal");
                return StatusCode(500, "Error allocating terminal");
            }
        }

        /// <summary>
        /// Release a terminal back to the pool
        /// </summary>
        [HttpPost("release/{id}")]
        public async Task<IActionResult> ReleaseTerminal(string id)
        {
            try
            {
                await _terminalService.ReleaseTerminalAsync(id);
                return Ok(new { Message = $"Terminal {id} released successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing terminal {Id}", id);
                return StatusCode(500, $"Error releasing terminal {id}");
            }
        }

        /// <summary>
        /// Refresh the session for a terminal
        /// </summary>
        [HttpPost("session/{id}/refresh")]
        public async Task<IActionResult> RefreshSession(string id)
        {
            try
            {
                await _terminalService.RefreshSessionAsync(id);
                await _terminalService.UpdateLastUsedTimeAsync(id);
                return Ok(new { Message = $"Session for terminal {id} refreshed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing session for terminal {Id}", id);
                return StatusCode(500, $"Error refreshing session for terminal {id}");
            }
        }
    }
}
