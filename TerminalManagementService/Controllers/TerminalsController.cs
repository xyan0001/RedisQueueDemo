using Microsoft.AspNetCore.Mvc;
using TerminalManagementService.Services;

namespace TerminalManagementService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TerminalsController(
    ITerminalService terminalService,
    ILogger<TerminalsController> logger,
    TerminalLifecycleSimulator simulator)
    : ControllerBase
{
    private readonly ITerminalService _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
    private readonly ILogger<TerminalsController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TerminalLifecycleSimulator _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));

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
                return NotFound("No terminals available");
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
    /// Clean up orphaned terminals
    /// </summary>
    /// <returns>Result of the cleanup operation</returns>
    [HttpPost("cleanup")]
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
            return StatusCode(500, "Error cleaning up terminals: " + ex.Message);
        }
    }

    /// <summary>
    /// Get the status of all terminals
    /// </summary>
    /// <returns>List of terminal statuses</returns>
    [HttpGet("statuses")]
    public async Task<IActionResult> GetTerminalStatuses()
    {
        try
        {
            var statuses = await _terminalService.GetTerminalStatusListAsync();
            return Ok(statuses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving terminal statuses");
            return StatusCode(500, "Error retrieving terminal statuses: " + ex.Message);
        }
    }

    /// <summary>
    /// Simulate a complete terminal lifecycle (allocate -> use -> release)
    /// </summary>
    /// <returns>Details about the simulation</returns>
    [HttpGet("simulate-single-lifecycle")]
    public async Task<IActionResult> SimulateSingleTerminalLifecycle()
    {
        try
        {
            _logger.LogInformation("Starting terminal lifecycle simulation");
            var result = await _simulator.SimulateTerminalLifecycleAsync();
            _logger.LogInformation("Terminal lifecycle simulation completed successfully");

            return Ok(new
            {
                Message = "Terminal lifecycle simulation completed successfully",
                SimulationResults = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during terminal lifecycle simulation");
            return StatusCode(500, "Error simulating terminal lifecycle: " + ex.Message);
        }
    }

    /// <summary>
    /// Run terminal lifecycle simulation
    /// </summary>
    /// <param name="iterations">Number of operations to perform</param>
    /// <param name="parallelism">Number of parallel operations</param>
    [HttpGet("lifecycle-simulation")]
    public async Task<IActionResult> RunLifecycleSimulation(
        [FromQuery] int iterations = 100,
        [FromQuery] int parallelism = 10)
    {
        try
        {
            _logger.LogInformation(
                "Starting terminal lifecycle simulation: {Iterations} iterations, {Parallelism} parallel operations",
                iterations, parallelism);

            // Run the simulation using the injected simulator
            var result = await _simulator.RunSimulationAsync(iterations, parallelism);

            return Ok(new
            {
                Message = "Terminal lifecycle simulation completed successfully",
                SimulationResults = result,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running terminal lifecycle simulation");
            return StatusCode(500, "Error running terminal lifecycle simulation: " + ex.Message);
        }
    }
}
