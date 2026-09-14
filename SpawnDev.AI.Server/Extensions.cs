using Microsoft.Extensions.DependencyInjection;

namespace SpawnDev.AI.Server;

/// <summary>DI registration for the in-browser AI server (Blazor WASM).</summary>
public static class SpawnDevAiServiceCollectionExtensions
{
    /// <summary>
    /// Register the SpawnDev.AI worker server + client. Call in ALL scopes (the same Program.cs runs
    /// in Window and Worker): the worker instance hosts <see cref="AiWorkerServer"/> on its GPU; the
    /// window instance uses <see cref="AiWorkerClient"/> to reach it. Requires
    /// <c>AddWebWorkerService()</c> and a registered <c>IModelSource</c> + <c>HttpClient</c>. A
    /// <c>WebTorrentClient</c> is OPTIONAL and no longer delivers models - see AiWorkerServer.
    /// </summary>
    public static IServiceCollection AddSpawnDevAI(this IServiceCollection services,
        Action<AiWorkerServerOptions> configure)
    {
        var options = new AiWorkerServerOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton<IAiWorkerApi, AiWorkerServer>();
        services.AddSingleton<AiWorkerClient>();
        return services;
    }
}
