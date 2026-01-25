using Microsoft.AspNetCore.Mvc;
using NAudio.CoreAudioApi;

namespace AudioController.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MicrophoneController(ILogger<MicrophoneController> logger) : ControllerBase
{
    [HttpGet("volume")]
    public IActionResult GetMicrophoneVolume()
    {
        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var defaultMicrophone = deviceEnumerator.GetDefaultAudioEndpoint(
                DataFlow.Capture,
                Role.Communications
            );

            if (defaultMicrophone == null)
            {
                return NotFound("No default microphone found");
            }

            var volume = defaultMicrophone.AudioEndpointVolume;
            var currentVolume = volume.MasterVolumeLevelScalar;
            var isMuted = volume.Mute;

            var result = new
            {
                Volume = currentVolume,
                VolumePercent = $"{currentVolume:P}",
                IsMuted = isMuted,
                IsAtMaximum = currentVolume >= 1.0f,
                DeviceName = defaultMicrophone.FriendlyName,
            };

            // Clean up
            defaultMicrophone.Dispose();
            deviceEnumerator.Dispose();

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get microphone volume");
            return StatusCode(500, "Failed to get microphone volume");
        }
    }

    [HttpPost("volume/max")]
    public IActionResult SetMicrophoneVolumeToMax()
    {
        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var defaultMicrophone = deviceEnumerator.GetDefaultAudioEndpoint(
                DataFlow.Capture,
                Role.Communications
            );

            if (defaultMicrophone == null)
            {
                return NotFound("No default microphone found");
            }

            var volume = defaultMicrophone.AudioEndpointVolume;
            var previousVolume = volume.MasterVolumeLevelScalar;

            volume.MasterVolumeLevelScalar = 1.0f;
            volume.Mute = false;

            var result = new
            {
                PreviousVolume = previousVolume,
                PreviousVolumePercent = $"{previousVolume:P}",
                CurrentVolume = 1.0f,
                CurrentVolumePercent = "100%",
                Message = "Microphone volume set to maximum",
            };

            // Clean up
            defaultMicrophone.Dispose();
            deviceEnumerator.Dispose();

            logger.LogInformation(
                "Microphone volume set to maximum. Previous: {PreviousVolume:P}",
                previousVolume
            );
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set microphone volume to maximum");
            return StatusCode(500, "Failed to set microphone volume to maximum");
        }
    }

    [HttpPost("volume/{volume:float}")]
    public IActionResult SetMicrophoneVolume(float volume)
    {
        if (volume < 0.0f || volume > 1.0f)
        {
            return BadRequest("Volume must be between 0.0 and 1.0");
        }

        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var defaultMicrophone = deviceEnumerator.GetDefaultAudioEndpoint(
                DataFlow.Capture,
                Role.Communications
            );

            if (defaultMicrophone == null)
            {
                return NotFound("No default microphone found");
            }

            var audioVolume = defaultMicrophone.AudioEndpointVolume;
            var previousVolume = audioVolume.MasterVolumeLevelScalar;

            audioVolume.MasterVolumeLevelScalar = volume;
            audioVolume.Mute = false;

            var result = new
            {
                PreviousVolume = previousVolume,
                PreviousVolumePercent = $"{previousVolume:P}",
                CurrentVolume = volume,
                CurrentVolumePercent = $"{volume:P}",
                Message = $"Microphone volume set to {volume:P}",
            };

            // Clean up
            defaultMicrophone.Dispose();
            deviceEnumerator.Dispose();

            logger.LogInformation(
                "Microphone volume set to {Volume:P}. Previous: {PreviousVolume:P}",
                volume,
                previousVolume
            );
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set microphone volume to {Volume}", volume);
            return StatusCode(500, "Failed to set microphone volume");
        }
    }
}
