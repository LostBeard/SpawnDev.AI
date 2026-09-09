using SpawnDev.AI.Server;
using SpawnDev.WebTorrent;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The catalogue tells the truth about what a model costs and whether it is already here.
/// </summary>
/// <remarks>
/// 🔴 THIS IS A CONSENT MECHANISM, NOT A CONVENIENCE. The picker asks the user to approve a download of up
/// to ~6.9 GB, and it asks using these numbers. A size that does not match reality means the agreement was
/// obtained under false information, and an approval that does not stick means being asked forever until
/// the question stops being read. Both are silent at runtime, which is why they are pinned here.
/// <para>
/// ⚠️ NOT heavy - nothing is loaded. It inspects the provider's own reporting, which is exactly the layer
/// the UI trusts.
/// </para>
/// </remarks>
public sealed class ModelCatalogueTests
{
    private readonly WebTorrentClient _webTorrent;
    private readonly HttpClient _http;
    private readonly SpawnDev.AsyncFileSystem.IAsyncFS _fs;

    /// <summary>New instance, over the app's own torrent client, HTTP client and filesystem.</summary>
    public ModelCatalogueTests(WebTorrentClient webTorrent, HttpClient http,
        SpawnDev.AsyncFileSystem.IAsyncFS fs)
    {
        _webTorrent = webTorrent;
        _http = http;
        _fs = fs;
    }

    private HubModelProvider Provider(params HubModelOption[] models)
        => new(_webTorrent, _http, models);

    /// <summary>Nothing is approved by default, and an approval survives a reload.</summary>
    /// <remarks>
    /// 🔴 DEFAULT-DENY IS THE WHOLE GUARD. Being wrong in this direction costs one extra click. Being
    /// wrong the other way starts a download of up to ~6.9 GB that nobody agreed to - and on a metered
    /// connection that is somebody's money.
    /// <para>
    /// ⚠️ Persistence is tested with a SECOND instance reading the same storage, because that is what a
    /// page reload is. An in-memory-only approval would pass any test that reused one instance, and would
    /// then re-prompt for every model on every visit.
    /// </para>
    /// </remarks>
    [AiTest(Timeout = 60_000)]
    public async Task NothingIsApprovedUntilTheUserSaysSoAndThenItSticks()
    {
        var name = "test:consent-" + Guid.NewGuid().ToString("N")[..8];
        var consent = new ModelConsent(_fs);
        await consent.LoadAsync();

        if (consent.IsApproved(name))
            throw new Exception("a model was approved before the user was ever asked");

        await consent.ApproveAsync(name);
        if (!consent.IsApproved(name))
            throw new Exception("approving a model did not take effect");

        // A fresh instance over the same storage IS a page reload.
        var reloaded = new ModelConsent(_fs);
        await reloaded.LoadAsync();
        if (!reloaded.IsApproved(name))
            throw new Exception("the approval did not survive a reload - every visit would re-prompt for "
                + "every model, and a user re-asked constantly stops reading the question");
        if (reloaded.IsApproved("test:never-approved"))
            throw new Exception("an unrelated model came back approved");

        await reloaded.ClearAsync();
        var afterClear = new ModelConsent(_fs);
        await afterClear.LoadAsync();
        if (afterClear.IsApproved(name))
            throw new Exception("clearing approvals did not stick, so a user cannot revoke consent");
    }

    /// <summary>The catalogue carries the size and description the picker shows.</summary>
    [AiTest(Timeout = 30_000)]
    public Task TheCatalogueCarriesSizeAndPurpose()
    {
        var p = Provider(
            new HubModelOption("a:one", "R/One", "one.gguf", ApproxSizeBytes: 500, Description: "the small one"),
            HubModelOption.FromOllama("b:two", "gemma4", "12b", 7_000, "the multimodal one"));

        var rows = p.Catalogue();
        if (rows.Count != 2) throw new Exception($"catalogue listed {rows.Count} of 2 models");

        var one = rows.First(r => r.Name == "a:one");
        if (one.SizeBytes != 500)
            throw new Exception($"size came back {one.SizeBytes} - the picker shows this to obtain consent");
        if (one.Description != "the small one")
            throw new Exception("the description was lost; a name and a byte count do not tell a user "
                + "whether a model can hold a character");

        // An ollama-sourced model must appear in the catalogue exactly like an HF one - the UI must not
        // need to know which registry a model came from.
        var two = rows.First(r => r.Name == "b:two");
        if (two.SizeBytes != 7_000 || two.Description != "the multimodal one")
            throw new Exception("an ollama-registry model lost its size or description");
        return Task.CompletedTask;
    }

    /// <summary>An ollama model keeps its coordinates and is distinguishable from a Hugging Face one.</summary>
    /// <remarks>
    /// ⚠️ The two sources load through DIFFERENT hub calls (<c>OpenOllamaAsync</c> vs <c>OpenAsync</c>).
    /// If <c>IsOllama</c> were wrong, gemma4 would be requested as a Hugging Face repo named "" and fail
    /// at load time - after the user had already agreed to a 6.9 GB download.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task AnOllamaModelKeepsItsCoordinates()
    {
        var ollama = HubModelOption.FromOllama("gemma4:12b", "gemma4", "12b", 7_408_798_105, "multimodal");
        if (!ollama.IsOllama) throw new Exception("an ollama model must report IsOllama");
        if (ollama.OllamaModel != "gemma4" || ollama.OllamaTag != "12b")
            throw new Exception($"coordinates came back {ollama.OllamaModel}:{ollama.OllamaTag}");
        if (ollama.CacheFileHint.Length == 0)
            throw new Exception("an ollama model needs a cache hint, or it can never read as downloaded");

        var hf = new HubModelOption("qwen3:1.7b-q8_0", "Qwen/Qwen3-1.7B-GGUF", "Qwen3-1.7B-Q8_0.gguf");
        if (hf.IsOllama) throw new Exception("a Hugging Face model must NOT report IsOllama - it would be "
            + "loaded through the wrong hub call");
        if (hf.CacheFileHint != "Qwen3-1.7B-Q8_0.gguf")
            throw new Exception($"an HF model's cache hint should be its file name, got '{hf.CacheFileHint}'");
        return Task.CompletedTask;
    }
}
