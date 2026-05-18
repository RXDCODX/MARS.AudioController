using MARS.AudioController.Models;
using MARS.AudioController.Services.TTS;
using Microsoft.AspNetCore.Mvc;

namespace MARS.AudioController.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TtsController(TtsPlaybackService ttsPlaybackService) : ControllerBase
{
    [HttpPost("play")]
    public async Task<IActionResult> Play([FromBody] TtsPlaybackRequest request)
    {
        IActionResult result = BadRequest(
            new TtsPlaybackResponse { Success = false, Message = "TTS playback failed" }
        );

        try
        {
            var playbackResult = await ttsPlaybackService.PlayAsync(request);
            result = playbackResult.Success ? Ok(playbackResult) : BadRequest(playbackResult);
        }
        catch (Exception ex)
        {
            result = BadRequest(new TtsPlaybackResponse { Success = false, Message = ex.Message });
        }

        return result;
    }
}