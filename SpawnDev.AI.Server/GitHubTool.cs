using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SpawnDev.AI.Server;

/// <summary>
/// A read-only GitHub lookup tool: lets the chat model answer questions about the SpawnDev libraries,
/// their code/docs, and the crew by fetching from GitHub. Deliberately host-ALLOWLISTED - every request
/// URL is built internally from a validated <c>owner/name</c> + path against api.github.com /
/// raw.githubusercontent.com only, so the model can never point it at an arbitrary host (no SSRF). Both
/// hosts send permissive CORS, so this works in the browser worker as-is. Anonymous (60 req/hr/IP), so
/// responses are cached in-process. Owner defaults to <c>LostBeard</c> (the SpawnDev author).
/// </summary>
public sealed class GitHubTool : IAiTool
{
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    /// <summary>Default repository owner when the model gives a bare name (e.g. "SpawnDev.BlazorJS").</summary>
    public string DefaultOwner { get; set; } = "LostBeard";

    /// <summary>Max characters of file/README content returned to the model (protects the small model's
    /// context window). The crew/credits section is preserved even when the body is truncated.</summary>
    public int MaxContentChars { get; set; } = 4000;

    public GitHubTool(HttpClient http) => _http = http;

    public string Name => "github_lookup";

    public string Description =>
        "Look up information on GitHub about the SpawnDev libraries, their code and docs, or the SpawnDev "
        + "crew/team. Call with no arguments to LIST all SpawnDev repositories and what each does; pass a "
        + "repo name to read that project's description and README; add a path to read a specific file. Use "
        + "this whenever the user asks about a SpawnDev library, how something works, releases, or who built it.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "repo": { "type": "string", "description": "Repository as 'owner/name' or just 'name' (owner defaults to LostBeard, the SpawnDev author). Omit to list ALL SpawnDev repositories with their descriptions." },
            "path": { "type": "string", "description": "Optional file path within the repo to read, e.g. 'README.md' or 'CHANGELOG.md'. Omit to read the repo's description + README." }
          }
        }
        """;

    public async Task<AiToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        string? repo = null, path = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("repo", out var r) && r.ValueKind == JsonValueKind.String) repo = r.GetString();
                if (doc.RootElement.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String) path = p.GetString();
            }
        }
        catch { /* malformed args - treat as "list repos" */ }

        try
        {
            if (string.IsNullOrWhiteSpace(repo)) return Ok(await ListReposAsync(ct).ConfigureAwait(false));

            if (!TryResolveRepo(repo, out var owner, out var name))
                return Err($"'{repo}' is not a valid repository name. Use 'owner/name' or just a repo name.");

            if (!string.IsNullOrWhiteSpace(path))
            {
                if (!IsSafePath(path)) return Err($"'{path}' is not an allowed file path.");
                var file = await GetRawAsync($"https://api.github.com/repos/{owner}/{name}/contents/{EncodePath(path)}", ct).ConfigureAwait(false);
                if (file == null) return Err($"'{owner}/{name}/{path}' was not found.");
                return Ok($"{owner}/{name}/{path}:\n\n{Truncate(file, out _)}");
            }

            // Repo overview: description + README (crew/credits preserved through truncation).
            var desc = await GetRepoDescriptionAsync(owner, name, ct).ConfigureAwait(false);
            var readme = await GetRawAsync($"https://api.github.com/repos/{owner}/{name}/readme", ct).ConfigureAwait(false);
            if (desc == null && readme == null) return Err($"Repository '{owner}/{name}' was not found.");
            var sb = new StringBuilder();
            sb.Append(owner).Append('/').Append(name);
            if (!string.IsNullOrWhiteSpace(desc)) sb.Append(" - ").Append(desc);
            if (readme != null)
            {
                var body = Truncate(readme, out bool cut);
                sb.Append("\n\nREADME:\n").Append(body);
                if (cut && ExtractSection(readme, "## The SpawnDev Crew") is { } crew && !body.Contains("SpawnDev Crew"))
                    sb.Append("\n\n").Append(crew);
            }
            return Ok(sb.ToString());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Err($"GitHub lookup failed: {ex.Message}"); }
    }

    // ── GitHub fetches (allowlisted hosts only; URLs are built from validated owner/name/path) ──
    private async Task<string> ListReposAsync(CancellationToken ct)
    {
        var json = await GetRawAsync($"https://api.github.com/users/{DefaultOwner}/repos?per_page=100&sort=full_name&type=owner", ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("could not list repositories");
        using var doc = JsonDocument.Parse(json);
        var lines = new List<string>();
        foreach (var repo in doc.RootElement.EnumerateArray())
        {
            if (repo.TryGetProperty("fork", out var f) && f.ValueKind == JsonValueKind.True) continue;
            string n = repo.TryGetProperty("name", out var nn) ? nn.GetString() ?? "" : "";
            string d = repo.TryGetProperty("description", out var dd) && dd.ValueKind == JsonValueKind.String ? dd.GetString() ?? "" : "";
            if (n.Length > 0) lines.Add(d.Length > 0 ? $"- {n}: {d}" : $"- {n}");
        }
        lines.Sort(StringComparer.OrdinalIgnoreCase);
        return $"SpawnDev repositories ({DefaultOwner}):\n" + string.Join("\n", lines);
    }

    private async Task<string?> GetRepoDescriptionAsync(string owner, string name, CancellationToken ct)
    {
        var json = await GetRawAsync($"https://api.github.com/repos/{owner}/{name}", ct).ConfigureAwait(false);
        if (json == null) return null;
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null; }
        catch { return null; }
    }

    /// <summary>GET an allowlisted GitHub URL, returning the body (raw for content endpoints, JSON for API
    /// endpoints) or null on 404/410. Cached in-process. GitHub requires a User-Agent; content endpoints get
    /// the raw media type so we receive file bytes rather than the base64 JSON envelope.</summary>
    private async Task<string?> GetRawAsync(string url, CancellationToken ct)
    {
        if (_cache.TryGetValue(url, out var cached)) return cached;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "SpawnDev.AI");
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github.raw+json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return null;
        if (resp.StatusCode == (HttpStatusCode)403)
            throw new InvalidOperationException("GitHub rate limit reached (anonymous: 60/hr). Try again later.");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _cache[url] = body;
        return body;
    }

    // ── Validation / formatting ──
    private bool TryResolveRepo(string repo, out string owner, out string name)
    {
        owner = DefaultOwner; name = "";
        repo = repo.Trim().Trim('/');
        var parts = repo.Split('/');
        if (parts.Length == 1) name = parts[0];
        else if (parts.Length == 2) { owner = parts[0]; name = parts[1]; }
        else return false;
        return IsSafeSegment(owner) && IsSafeSegment(name);
    }

    private static bool IsSafeSegment(string s)
        => s.Length is > 0 and <= 100 && s.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');

    private static bool IsSafePath(string path)
        => path.Length <= 300 && !path.Contains("..") && !path.StartsWith('/')
           && path.Split('/').All(seg => seg.Length == 0 || seg.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or ' '));

    private static string EncodePath(string path)
        => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private string Truncate(string s, out bool cut)
    {
        if (s.Length <= MaxContentChars) { cut = false; return s; }
        cut = true;
        return s[..MaxContentChars] + "\n...[truncated]...";
    }

    // Grab a markdown section (heading line through the next same-or-higher heading or end of doc).
    private static string? ExtractSection(string markdown, string heading)
    {
        int i = markdown.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        int end = markdown.IndexOf("\n## ", i + heading.Length, StringComparison.Ordinal);
        var section = end < 0 ? markdown[i..] : markdown[i..end];
        return section.Length <= 1500 ? section : section[..1500];
    }

    private static AiToolExecutionResult Ok(string text) => new(text);
    private static AiToolExecutionResult Err(string text) => new(text) { IsError = true };
}
