using Microsoft.AspNetCore.Mvc;
using TerminalManagementService.Services;

namespace TerminalManagementService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController(
    ITerminalService terminalService,
    ILogger<AdminController> logger)
    : ControllerBase
{
    private readonly ITerminalService _terminalService =
        terminalService ?? throw new ArgumentNullException(nameof(terminalService));

    private readonly ILogger<AdminController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

    /// <summary>
    /// Get cache metrics
    /// </summary>
    [HttpGet("cache/metrics")]
    public IActionResult GetCacheMetrics()
    {
        try
        {
            var metrics = _terminalService.GetCacheMetrics();
            return Ok(new
            {
                Hits = metrics.hits,
                Misses = metrics.misses,
                HitRate = Math.Round(metrics.hitRate, 2),
                TotalRequests = metrics.hits + metrics.misses,
                CacheStatus = metrics.hits + metrics.misses > 0 ? "Active" : "No requests yet"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache metrics");
            return StatusCode(500, "Error getting cache metrics");
        }
    }

    /// <summary>
    /// Run cache performance test
    /// </summary>
    [HttpGet("cache/performance-test")]
    public async Task<IActionResult> RunCachePerformanceTest()
    {
        try
        {
            // Create an instance of the test class and run tests with the existing terminal service
            await new CachePerformanceTest(_terminalService, _logger).RunTestsAsync();

            // Get updated metrics
            var metrics = _terminalService.GetCacheMetrics();

            return Ok(new
            {
                Message = "Cache performance test completed successfully",
                Metrics = new
                {
                    Hits = metrics.hits,
                    Misses = metrics.misses,
                    HitRate = Math.Round(metrics.hitRate, 2),
                    TotalRequests = metrics.hits + metrics.misses
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running cache performance test");
            return StatusCode(500, "Error running cache performance test");
        }
    }
}

public class AddTerminalsRequest
{
    /// <summary>
    /// Starting index for new terminals
    /// </summary>
    public int StartIndex { get; set; }        /// <summary>
                                               /// Number of terminals to add
                                               /// </summary>
    public int Count { get; set; }
}
