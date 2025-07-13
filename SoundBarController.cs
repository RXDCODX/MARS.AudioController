using Microsoft.AspNetCore.Mvc;

namespace AudioController;

[ApiController]
[Route("api/[controller]")]
public class SoundBarController(AudioControllerService audioService) : ControllerBase
{
    [HttpPost("mute")]
    public async Task<IActionResult> Mute([FromBody] MuteRequest request)
    {
        try
        {
            await audioService.MuteAll([.. request.ProcessNames]);
            return Ok(new { success = true, message = "Audio muted successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("unmute")]
    public async Task<IActionResult> Unmute()
    {
        try
        {
            await audioService.UnMuteAll();
            return Ok(new { success = true, message = "Audio unmuted successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("bagcount")]
    public IActionResult GetBagCount()
    {
        try
        {
            var bagCount = audioService.GetBagCount();
            return Ok(new { success = true, bagCount });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}

public class MuteRequest
{
    public List<string> ProcessNames { get; set; } = [];
}
