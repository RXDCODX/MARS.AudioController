using Microsoft.AspNetCore.Mvc;
using AudioController.Models;
using AudioController.Services;

namespace AudioController.Controllers;

/// <summary>
/// Controller for audio playback queue management
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AudioPlaybackController(IAudioPlaybackQueueService audioPlaybackService) : ControllerBase
{
    /// <summary>
    /// Queue audio for playback
    /// </summary>
    /// <param name="request">Audio playback request with URL</param>
    /// <returns>Queue status</returns>
    [HttpPost("queue")]
    public async Task<IActionResult> QueueAudio([FromBody] AudioPlaybackRequest request)
    {
        try
        {
            var response = await audioPlaybackService.QueueAudioAsync(request);
            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new AudioPlaybackResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Get current playback status
    /// </summary>
    /// <returns>Current status of audio playback</returns>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        try
        {
            var status = audioPlaybackService.GetStatus();
            return Ok(status);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Skip current audio and play next in queue
    /// </summary>
    /// <returns>Success or error message</returns>
    [HttpPost("skip")]
    public async Task<IActionResult> Skip()
    {
        try
        {
            await audioPlaybackService.SkipCurrentAsync();
            return Ok(new { success = true, message = "Skipped to next audio" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Stop playback and clear queue
    /// </summary>
    /// <returns>Success or error message</returns>
    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        try
        {
            await audioPlaybackService.StopAndClearAsync();
            return Ok(new { success = true, message = "Playback stopped and queue cleared" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Pause current playback
    /// </summary>
    /// <returns>Success or error message</returns>
    [HttpPost("pause")]
    public IActionResult Pause()
    {
        try
        {
            audioPlaybackService.Pause();
            return Ok(new { success = true, message = "Playback paused" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Resume paused playback
    /// </summary>
    /// <returns>Success or error message</returns>
    [HttpPost("resume")]
    public IActionResult Resume()
    {
        try
        {
            audioPlaybackService.Resume();
            return Ok(new { success = true, message = "Playback resumed" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get queue items
    /// </summary>
    /// <returns>List of queued audio items</returns>
    [HttpGet("queue")]
    public IActionResult GetQueue()
    {
        try
        {
            var items = audioPlaybackService.GetQueueItems();
            return Ok(new { success = true, queueItems = items, count = items.Count });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get current volume (0-100)
    /// </summary>
    /// <returns>Current volume level</returns>
    [HttpGet("volume")]
    public IActionResult GetVolume()
    {
        try
        {
            var volume = audioPlaybackService.GetVolume();
            return Ok(new { success = true, volume = volume });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Set volume (0-100)
    /// </summary>
    /// <param name="volume">Volume level 0-100</param>
    /// <returns>Success or error message</returns>
    [HttpPost("volume")]
    public IActionResult SetVolume([FromQuery] int volume)
    {
        try
        {
            // Validate volume range
            if (volume < 0 || volume > 100)
            {
                return BadRequest(new { success = false, message = "Volume must be between 0 and 100" });
            }

            audioPlaybackService.SetVolume(volume);
            return Ok(new { success = true, message = $"Volume set to {volume}%", volume = volume });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
