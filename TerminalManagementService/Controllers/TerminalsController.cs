using Microsoft.AspNetCore.Mvc;
using TerminalManagementService.Services;

namespace TerminalManagementService.Controllers;
[ApiController]
[Route("api/[controller]")]
public class TerminalsController(
    ITerminalService terminalService,
    ILogger<TerminalsController> logger)
    : ControllerBase
{
    private readonly ITerminalService _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
    private readonly ILogger<TerminalsController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
                terminal.Address,
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

    /// <summary>
    /// Simulate a complete terminal lifecycle (allocate -> use -> release)
    /// </summary>
    /// <param name="simulatedResponseDelayMs">Optional delay in milliseconds to simulate terminal usage time</param>
    /// <returns>Details about the simulation</returns>
    [HttpPost("simulate-lifecycle")]
    public async Task<IActionResult> SimulateTerminalLifecycle([FromQuery] int simulatedResponseDelayMs = 500)
    {
        try
        {
            var startTime = DateTimeOffset.UtcNow;
            _logger.LogInformation("Starting terminal lifecycle simulation");

            // Step 1: Allocate a terminal
            _logger.LogInformation("Step 1: Allocating terminal");
            var terminal = await _terminalService.AllocateTerminalAsync();
            if (terminal == null)
            {
                return StatusCode(503, "No terminals available for simulation");
            }

            // Step 2: Get or create a session
            _logger.LogInformation("Step 2: Creating/getting session for terminal {TerminalId}", terminal.Id);
            var sessionId = await _terminalService.GetOrCreateSessionAsync(terminal.Id);

            // Step 3: Simulate using the terminal (making a request)
            _logger.LogInformation("Step 3: Simulating terminal usage with {TerminalId}, delay: {Delay}ms", 
                terminal.Id, simulatedResponseDelayMs);
            
            // Simulate a terminal operation by waiting
            await Task.Delay(simulatedResponseDelayMs);
            
            // Update last used time to show activity
            await _terminalService.UpdateLastUsedTimeAsync(terminal.Id);
            
            // Step 4: Release the terminal back to the pool
            _logger.LogInformation("Step 4: Releasing terminal {TerminalId} back to the pool", terminal.Id);
            await _terminalService.ReleaseTerminalAsync(terminal.Id);

            var endTime = DateTimeOffset.UtcNow;
            var duration = endTime - startTime;

            // Return details about the simulation
            return Ok(new 
            {
                Success = true,
                TerminalId = terminal.Id,
                SessionId = sessionId,
                SimulationSteps = new[]
                {
                    "1. Allocated terminal from pool",
                    "2. Created/retrieved session for terminal",
                    "3. Simulated terminal usage (request/response)",
                    "4. Released terminal back to the pool"
                },
                Duration = $"{duration.TotalMilliseconds:F2}ms",
                StartTime = startTime,
                EndTime = endTime
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during terminal lifecycle simulation");
            return StatusCode(500, "Error simulating terminal lifecycle: " + ex.Message);
        }
    }
}
