using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;

namespace MARS.AudioController.Services.Obs;

public class ObsService(
    IOBSWebsocket obs,
    IOptions<ObsConfiguration> config,
    ILogger<ObsService> logger
) : IObsService, IDisposable
{
    private readonly ObsConfiguration _config = config.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _savedSceneBeforePause;
    private bool _disposed;

    public bool IsConnected => obs.IsConnected;

    public bool IsPaused { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var url = $"ws://{_config.Host}:{_config.Port}";

        try
        {
            if (obs.IsConnected)
            {
                logger.LogDebug("Already connected to OBS");
                return Task.CompletedTask;
            }

            logger.LogInformation("Connecting to OBS at {Url}", url);

            obs.ConnectAsync(url, _config.Password);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to OBS at {Url}", url);
            throw;
        }

        return Task.CompletedTask;
    }

    public void DisconnectAsync()
    {
        try
        {
            if (obs.IsConnected)
            {
                obs.Disconnect();
                logger.LogInformation("Disconnected from OBS");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during OBS disconnect");
        }
    }

    public async Task<string> ScreenshotAsync(
        string? sourceName = null,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureConnectedAsync();

        var sceneName = sourceName ?? obs.GetCurrentProgramScene();
        var screenshotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
        Directory.CreateDirectory(screenshotDir);

        var fileName = $"obs_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.webp";
        var filePath = Path.Combine(screenshotDir, fileName);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            obs.SaveSourceScreenshot(
                sceneName,
                "webp",
                filePath,
                -1,
                -1,
                _config.ScreenshotQuality
            );

            logger.LogDebug("Screenshot saved to {Path}", filePath);

            return filePath;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ObsPauseResult> FreezeAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync();

            if (IsPaused)
            {
                return ObsPauseResult.Fail("Already paused");
            }

            var currentScene = obs.GetCurrentProgramScene();
            _savedSceneBeforePause = currentScene;

            EnsurePauseSceneSetup();

            var screenshotPath = await TakeScreenshotInternalAsync(currentScene);

            UpdatePauseImageSource(screenshotPath);
            ShowPauseScreenScene(currentScene);

            IsPaused = true;
            logger.LogInformation("Pause activated on scene {Scene}", currentScene);

            return ObsPauseResult.Ok(screenshotPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to activate pause");
            return ObsPauseResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ObsPauseResult> UnfreezeAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync();

            if (!IsPaused)
            {
                return ObsPauseResult.Fail("Not paused");
            }

            var scene = _savedSceneBeforePause ?? obs.GetCurrentProgramScene();

            HidePauseScreenScene(scene);
            RemovePauseInput();

            IsPaused = false;
            _savedSceneBeforePause = null;

            logger.LogInformation("Pause deactivated");

            return ObsPauseResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deactivate pause");
            return ObsPauseResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ObsPauseResult> SwitchToPauseSceneAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync();

            if (IsPaused)
            {
                return ObsPauseResult.Fail("Already paused");
            }

            var currentScene = obs.GetCurrentProgramScene();
            _savedSceneBeforePause = currentScene;

            var screenshotPath = await TakeScreenshotInternalAsync(currentScene);

            UpdatePauseImageSource(screenshotPath);

            obs.SetCurrentProgramScene(_config.PauseSceneName);

            IsPaused = true;
            logger.LogInformation("Switched to pause scene (from {Scene})", currentScene);

            return ObsPauseResult.Ok(screenshotPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to switch to pause scene");
            return ObsPauseResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ObsPauseResult> SwitchFromPauseSceneAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync();

            if (!IsPaused)
            {
                return ObsPauseResult.Fail("Not paused");
            }

            var targetScene = _savedSceneBeforePause ?? obs.GetCurrentProgramScene();

            obs.SetCurrentProgramScene(targetScene);

            IsPaused = false;
            _savedSceneBeforePause = null;

            logger.LogInformation("Switched from pause scene to {Scene}", targetScene);

            return ObsPauseResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to switch from pause scene");
            return ObsPauseResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ObsPauseResult> TogglePauseAsync(
        ObsPauseMode mode = ObsPauseMode.FreezeFrame,
        CancellationToken cancellationToken = default
    )
    {
        return mode switch
        {
            ObsPauseMode.FreezeFrame => IsPaused
                ? await UnfreezeAsync(cancellationToken)
                : await FreezeAsync(cancellationToken),
            ObsPauseMode.PauseScene => IsPaused
                ? await SwitchFromPauseSceneAsync(cancellationToken)
                : await SwitchToPauseSceneAsync(cancellationToken),
            _ => ObsPauseResult.Fail($"Unknown pause mode: {mode}"),
        };
    }

    private Task<string> TakeScreenshotInternalAsync(string sceneName)
    {
        var screenshotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
        Directory.CreateDirectory(screenshotDir);

        var fileName = $"obs_pause_{DateTime.Now:yyyyMMdd_HHmmss}.webp";
        var filePath = Path.Combine(screenshotDir, fileName);

        obs.SaveSourceScreenshot(sceneName, "webp", filePath, -1, -1, _config.ScreenshotQuality);

        return Task.FromResult(filePath);
    }

    private void ShowPauseScreenScene(string sceneName)
    {
        try
        {
            var itemId = obs.GetSceneItemId(sceneName, _config.PauseSceneName, 0);
            obs.SetSceneItemEnabled(sceneName, itemId, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Pause screen scene '{PauseScene}' not found in scene '{Scene}'",
                _config.PauseSceneName,
                sceneName
            );
        }
    }

    private void HidePauseScreenScene(string scenename)
    {
        try
        {
            var itemId = obs.GetSceneItemId(scenename, _config.PauseSceneName, 0);
            obs.SetSceneItemEnabled(scenename, itemId, false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Pause screen scene '{PauseScene}' not found in scene",
                _config.PauseSceneName
            );
        }
    }

    private void EnsurePauseSceneSetup()
    {
        try
        {
            obs.GetInputSettings(_config.PauseImageSourceName);
            return;
        }
        catch { }

        try
        {
            var defaultSettings = new JObject { { "file", "" } };
            obs.CreateInput(
                _config.PauseSceneName,
                _config.PauseImageSourceName,
                "image_source",
                defaultSettings,
                true
            );

            logger.LogInformation(
                "Created pause image source '{Source}' in scene '{Scene}'",
                _config.PauseImageSourceName,
                _config.PauseSceneName
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to create pause image source '{Source}'",
                _config.PauseImageSourceName
            );
        }
    }

    private void RemovePauseInput()
    {
        try
        {
            obs.RemoveInput(_config.PauseImageSourceName);
            logger.LogInformation(
                "Removed pause image source '{Source}'",
                _config.PauseImageSourceName
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to remove pause image source '{Source}'",
                _config.PauseImageSourceName
            );
        }
    }

    private void UpdatePauseImageSource(string imagePath)
    {
        try
        {
            var settings = new JObject { { "file", imagePath } };
            obs.SetInputSettings(_config.PauseImageSourceName, settings, true);

            var itemId = obs.GetSceneItemId(
                _config.PauseSceneName,
                _config.PauseImageSourceName,
                0
            );
            obs.SetSceneItemEnabled(_config.PauseSceneName, itemId, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to update pause image source '{Source}'",
                _config.PauseImageSourceName
            );
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (obs.IsConnected)
        {
            return;
        }

        logger.LogWarning("OBS not connected, attempting to reconnect...");

        try
        {
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reconnect attempt failed");
        }

        for (var i = 0; i < 50; i++)
        {
            if (obs.IsConnected)
            {
                logger.LogInformation("Reconnected to OBS successfully");
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("Not connected to OBS");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        obs?.Disconnect();
        _lock?.Dispose();
        GC.SuppressFinalize(this);
    }
}
