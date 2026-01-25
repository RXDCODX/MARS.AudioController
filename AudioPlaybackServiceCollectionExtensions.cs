using AudioController.Services;

namespace AudioController;

/// <summary>
/// Extension methods for registering audio playback services
/// </summary>
public static class AudioPlaybackServiceCollectionExtensions
{
    /// <summary>
    /// Adds audio playback queue service to the dependency injection container
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAudioPlaybackQueue(this IServiceCollection services)
    {
        services.AddHttpClient<IAudioPlaybackQueueService, AudioPlaybackQueueService>();
        return services;
    }

    /// <summary>
    /// Adds audio playback queue service with custom HTTP client configuration
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureClient">Action to configure the HTTP client</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAudioPlaybackQueue(
        this IServiceCollection services,
        Action<HttpClient> configureClient)
    {
        services.AddHttpClient<IAudioPlaybackQueueService, AudioPlaybackQueueService>()
            .ConfigureHttpClient(configureClient);
        return services;
    }
}
