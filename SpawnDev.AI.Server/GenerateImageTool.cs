using System.Text.Json;

namespace SpawnDev.AI.Server;

/// <summary>
/// The built-in image-generation tool: lets the LLM produce images mid-conversation. The image
/// bytes go to the artifact store (PNG); the model reads a short confirmation referencing
/// "ai-artifact://{id}" which UIs resolve to the actual image inline.
/// </summary>
public sealed class GenerateImageTool : IAiTool
{
    private readonly AiImageEngine _images;
    private readonly AiToolRegistry _registry;

    public GenerateImageTool(AiImageEngine images, AiToolRegistry registry)
    {
        _images = images;
        _registry = registry;
    }

    public string Name => "generate_image";

    public string Description =>
        "Generate an image from a text prompt using the local on-device diffusion model. "
        + "Use when the user asks for a picture, drawing, photo, or any visual.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "prompt": { "type": "string", "description": "What the image should depict, phrased as a caption (e.g. 'a photo of a red fox in snow')." },
            "seed": { "type": "integer", "description": "Optional seed for reproducibility." }
          },
          "required": ["prompt"]
        }
        """;

    public async Task<AiToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            string prompt = doc.RootElement.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
            int? seed = doc.RootElement.TryGetProperty("seed", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32() : null;
            if (string.IsNullOrWhiteSpace(prompt))
                return new AiToolExecutionResult("Error: 'prompt' is required.") { IsError = true };

            var img = await _images.GenerateAsync(prompt, seed: seed, ct: ct).ConfigureAwait(false);
            var png = PngEncoder.EncodeRgba(img.Rgba, img.Width, img.Height);
            string id = Guid.NewGuid().ToString("N")[..12];
            _registry.StoreArtifact(new AiToolArtifact(id, "image/png", png, prompt));

            return new AiToolExecutionResult(
                $"Image generated ({img.Width}x{img.Height}, model {img.Model}, seed {img.Seed}, "
                + $"{img.InferenceMs:F0}ms). It is displayed to the user as ai-artifact://{id} - "
                + "describe it briefly; do not repeat the artifact id.",
                new[] { new AiToolArtifact(id, "image/png", png, prompt) });
        }
        catch (Exception ex)
        {
            return new AiToolExecutionResult($"Image generation failed: {ex.Message}") { IsError = true };
        }
    }
}
