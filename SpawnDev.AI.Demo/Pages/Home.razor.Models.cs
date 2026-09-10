using Microsoft.AspNetCore.Components;
using SpawnDev.AI;
using SpawnDev.AI.Server;

namespace SpawnDev.AI.Demo.Pages;

/// <summary>
/// Choosing a model, and never downloading gigabytes without being asked.
/// </summary>
/// <remarks>
/// 🔴 THE POINT OF THIS FILE. Before it, the first message on a freshly picked model silently pulled the
/// weights - the user learned the size of their choice from their connection, not from the app, and the
/// biggest model here is ~6.9 GB. Now every model states its size, and one the user has not agreed to
/// does not begin downloading until they press the button that says so.
/// </remarks>
public partial class Home
{
    [Inject] ModelConsent Consent { get; set; } = default!;

    /// <summary>Every model the server can serve, with its size and what it is for.</summary>
    List<AiModelChoice> _catalogue = new();

    /// <summary>The model currently being fetched, or empty.</summary>
    string _downloadingModel = "";

    bool _showModels;

    /// <summary>
    /// Refresh the catalogue.
    /// </summary>
    /// <remarks>
    /// Cheap: metadata only. Sizes and descriptions come from the server; whether the user has AGREED to
    /// a given model is ours to remember, and comes from <see cref="ModelConsent"/>.
    /// </remarks>
    async Task LoadCatalogueAsync()
    {
        await Consent.LoadAsync();
        try { _catalogue = await Ai.GetModelCatalogueAsync(); }
        catch (Exception ex) { Console.WriteLine($"[models] catalogue unavailable: {ex.Message}"); }
    }

    /// <summary>Has the user agreed to fetch this model?</summary>
    bool IsApproved(string name) => Consent.IsApproved(name);

    /// <summary>How much of a model is already here, as a short label, or empty when none/unknown.</summary>
    /// <remarks>
    /// Shown, never acted on: see <c>HubModelProvider.CachedFraction</c> for why this cannot be turned
    /// into "already downloaded".
    /// </remarks>
    string CachedLabel(string name)
        => ChoiceFor(name)?.CachedFraction is { } f && f > 0.01 ? $" · {f:P0} cached" : "";

    /// <summary>What the catalogue says about a model, or null when it says nothing.</summary>
    AiModelChoice? ChoiceFor(string name) => _catalogue.FirstOrDefault(c => c.Name == name);

    /// <summary>
    /// The registered options - the SAME model list the worker serves, readable with no worker running.
    /// </summary>
    /// <remarks>
    /// 🔴 WHY THIS IS INJECTED SEPARATELY FROM THE CATALOGUE. Program.cs runs in BOTH scopes, so the
    /// window holds the identical <see cref="AiWorkerServerOptions.Models"/> the worker will serve. That
    /// matters for exactly one moment: the landing page, where <see cref="_catalogue"/> is still empty
    /// because it is fetched from a worker that has not been started yet.
    /// </remarks>
    [Inject] AiWorkerServerOptions AiOptions { get; set; } = default!;

    /// <summary>
    /// Download size of a model, from the catalogue if the worker is up and from the registered options
    /// if it is not. Zero when nothing knows.
    /// </summary>
    /// <remarks>
    /// 🔴 THE CONSENT CLAIM DEPENDS ON THIS. "Pressing Start the AI server is the agreement for the model
    /// that server will run - its size is on the button" is only true if a size can be shown BEFORE the
    /// button is pressed, and <see cref="ChoiceFor"/> alone cannot: the catalogue is loaded inside
    /// StartAsync, so on the landing page it is empty and the button silently dropped the size and read
    /// plain "Start the AI server". A first-time visitor agreed to a download whose size they were never
    /// told - the precise thing this file exists to prevent - and it got worse the moment the default
    /// model became the 1.8 GB one that can actually hold a character.
    /// </remarks>
    long SizeOfModel(string name)
        => ChoiceFor(name)?.SizeBytes
           ?? AiOptions.Models.FirstOrDefault(m => m.Name == name)?.ApproxSizeBytes
           ?? 0;

    /// <summary>
    /// True when picking this model would start a download.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Returns FALSE for a model the catalogue knows nothing about, and that is deliberate. If the
    /// catalogue could not be read we do not know what a download would cost, and blocking chat on an
    /// unknown would make a failed metadata call look like a broken app. The user is only ever stopped
    /// when there is a real size to tell them about.
    /// </para>
    /// <para>
    /// 🔴 ASKS ABOUT CONSENT, NOT ABOUT CACHE STATE. Whether the bytes are already local cannot be known
    /// (see <see cref="ModelConsent"/>); whether the user agreed to this model can. Approving once is
    /// permanent, so a browser eviction re-fetches under an approval already given rather than
    /// re-prompting.
    /// </para>
    /// </remarks>
    bool WouldDownload(string name) => ChoiceFor(name) != null && !IsApproved(name);

    /// <summary>Human size for the picker. Sizes here are what the user consents to, so they are explicit.</summary>
    static string FormatSize(long bytes)
        => bytes <= 0 ? "size unknown"
         : bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.#} GB"
         : $"{bytes / 1_048_576.0:0} MB";

    /// <summary>
    /// Fetch and load a model, on purpose, because the user asked for it.
    /// </summary>
    /// <remarks>
    /// Uses <c>POST /api/warm</c> (kinds: chat) - the server's own preload path, which streams the weights
    /// and makes the model resident. Deliberately NOT a one-token throwaway generation: that would work by
    /// accident and would put a junk turn in the transcript.
    /// </remarks>
    async Task DownloadAndLoadAsync(string name)
    {
        if (_downloadingModel.Length > 0) return;   // one multi-GB fetch at a time
        var choice = ChoiceFor(name);
        _downloadingModel = name;
        _status = choice != null && !IsApproved(name)
            ? $"Downloading {name} ({FormatSize(choice.SizeBytes)})… this happens once, then it is cached."
            : $"Loading {name}…";
        // Pressing the button IS the agreement, and it is recorded before the fetch starts - a download
        // interrupted half way must not ask again as though nothing had been agreed.
        await Consent.ApproveAsync(name);
        StateHasChanged();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (warmed, failed) = await Ai.WarmAsync(new[] { "chat" }, name);
            clock.Stop();
            if (failed.Length > 0)
            {
                // Name the model and the reason. "Loading failed" against a 7 GB download tells the user
                // nothing about whether to retry, pick something smaller, or free up storage.
                _status = $"{name} could not be loaded: {failed[0].Error}";
                _messages.Add(new Msg { Role = "system", Text = _status });
            }
            else
            {
                _model = name;
                _status = $"{name} is ready ({clock.Elapsed.TotalSeconds:F0}s) and is now the chat model.";
            }
        }
        catch (Exception ex)
        {
            _status = $"{name} could not be loaded: {ex.Message}";
            _messages.Add(new Msg { Role = "system", Text = _status });
        }
        finally
        {
            _downloadingModel = "";
            await LoadCatalogueAsync();
            StateHasChanged();
        }
    }

    /// <summary>
    /// Stop a turn that would silently start a multi-GB download, and say what to do instead.
    /// </summary>
    /// <returns>True when the turn may proceed.</returns>
    /// <remarks>
    /// 🔴 THIS IS THE ACTUAL GUARD. The picker is only advice; this is what makes a big download
    /// impossible to trigger by accident, because the download otherwise happens deep inside the first
    /// generation where nothing has asked the user anything.
    /// </remarks>
    bool EnsureModelIsHere()
    {
        if (!WouldDownload(_model)) return true;
        var choice = ChoiceFor(_model)!;
        _showModels = true;
        _status = $"{_model} is not on this device yet ({FormatSize(choice.SizeBytes)} to download).";
        _messages.Add(new Msg
        {
            Role = "system",
            Text = $"**{_model}** is {FormatSize(choice.SizeBytes)} and you have not agreed to fetch it "
                 + "yet. Nothing large is downloaded without asking, so press **Download & load** in the "
                 + "model panel (📦) when you are ready, or pick a model you have already approved.",
        });
        StateHasChanged();
        return false;
    }
}
