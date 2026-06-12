using Microsoft.AspNetCore.Mvc;

namespace MARS.AudioController.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { success = true, message = "OK" });
    }
}
