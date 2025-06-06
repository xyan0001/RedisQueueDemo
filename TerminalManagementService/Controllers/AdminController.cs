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
    public class AdminController : ControllerBase
    {
        private readonly ITerminalService _terminalService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            ITerminalService terminalService,
            ILogger<AdminController> logger)
        {
            _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Add new terminals to the pool
        /// </summary>
        [HttpPost("terminals/add")]
        public async Task<IActionResult> AddTerminals([FromBody] AddTerminalsRequest request)
        {
            try
            {
                if (request.Count <= 0)
                {
                    return BadRequest("Count must be greater than 0");
                }

                await _terminalService.AddTerminalsAsync(request.StartIndex, request.Count);
                return Ok(new { Message = $"Added {request.Count} new terminals starting at index {request.StartIndex}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding new terminals");
                return StatusCode(500, "Error adding new terminals");
            }
        }

        /// <summary>
        /// Force cleanup of orphaned terminals
        /// </summary>
        [HttpPost("terminals/cleanup")]
        public async Task<IActionResult> CleanupTerminals()
        {
            try
            {
                await _terminalService.ReclaimOrphanedTerminalsAsync();
                return Ok(new { Message = "Terminal cleanup completed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up terminals");
                return StatusCode(500, "Error cleaning up terminals");
            }
        }
    }

    public class AddTerminalsRequest
    {
        /// <summary>
        /// Starting index for new terminals
        /// </summary>
        public int StartIndex { get; set; }

        /// <summary>
        /// Number of terminals to add
        /// </summary>
        public int Count { get; set; }
    }
}
