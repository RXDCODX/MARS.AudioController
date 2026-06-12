using Microsoft.AspNetCore.Mvc;

namespace MARS.AudioController.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(IHostApplicationLifetime lifetime) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { success = true, message = "OK" });
    }

    [HttpPost("shutdown")]
    public IActionResult Shutdown()
    {
        lifetime.StopApplication();
        return Ok(new { success = true, message = "AudioController shutting down" });
    }
}
