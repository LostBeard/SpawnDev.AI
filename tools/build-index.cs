// Generates spawndev-index.md - a single, CDN-served (raw.githubusercontent.com) digest of the SpawnDev
// libraries + crew so the in-browser AI can ground SpawnDev answers from ONE cached request instead of many
// api.github.com calls (anonymous limit is 60/hr per user IP). Run by .github/workflows/build-spawndev-index.yml
// (twice daily) with GITHUB_TOKEN for a 5000/hr limit; also runs locally anonymously.
//   dotnet run tools/build-index.cs [outfile]      (default: spawndev-index.md at repo root)
using System.Text;
using System.Text.Json;

string outFile = args.Length > 0 ? args[0] : "spawndev-index.md";
string owner = "LostBeard";
string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

using var http = new HttpClient();
http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SpawnDev.AI-index-builder");
if (!string.IsNullOrEmpty(token)) http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token}");

async Task<string> ApiAsync(string url, string accept)
{
    using var req = new HttpRequestMessage(HttpMethod.Get, url);
    req.Headers.TryAddWithoutValidation("Accept", accept);
    using var resp = await http.SendAsync(req);
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadAsStringAsync();
}
async Task<string?> TryRawAsync(string url)
{
    try { return await ApiAsync(url, "application/vnd.github.raw+json"); } catch { return null; }
}

Console.WriteLine($"[index] listing {owner} repos...");
var reposJson = await ApiAsync($"https://api.github.com/users/{owner}/repos?per_page=100&sort=full_name&type=owner", "application/vnd.github+json");
using var doc = JsonDocument.Parse(reposJson);

var repos = new List<(string Name, string Desc)>();
foreach (var r in doc.RootElement.EnumerateArray())
{
    if (r.TryGetProperty("fork", out var f) && f.ValueKind == JsonValueKind.True) continue;   // originals, not forks
    string name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
    if (name.Length == 0 || name.Equals(owner, StringComparison.OrdinalIgnoreCase)) continue;  // skip the profile repo
    if (IsNoise(name)) continue;                                                                // skip bug-repro/test/demo repos
    string desc = r.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
    repos.Add((name, desc));
}
repos.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
Console.WriteLine($"[index] {repos.Count} SpawnDev repos");

// Per-repo README excerpt (first real prose after title/badges), and the crew section from the first README that has one.
var excerpts = new Dictionary<string, string>();
string? crew = null;
foreach (var (name, _) in repos)
{
    var readme = await TryRawAsync($"https://api.github.com/repos/{owner}/{name}/readme");
    if (readme == null) continue;
    excerpts[name] = Excerpt(readme, 600);
    crew ??= ExtractSection(readme, "## The SpawnDev Crew");
}

var sb = new StringBuilder();
sb.AppendLine("# SpawnDev Library Index");
sb.AppendLine();
sb.AppendLine("Auto-generated digest of the SpawnDev open-source libraries and the crew who builds them. This is");
sb.AppendLine("the AI's single-request grounding source. For a library's full README, changelog, or a specific");
sb.AppendLine("file, call github_lookup with its repo name (and optional path).");
sb.AppendLine();
void Bullets(IEnumerable<(string Name, string Desc)> set)
{
    foreach (var (name, desc) in set)
        sb.AppendLine(desc.Length > 0 ? $"- **{name}** ({owner}/{name}) - {desc}" : $"- **{name}** ({owner}/{name})");
}
// SpawnDev libraries are the primary focus; the rest are apps/projects built WITH SpawnDev that show it off.
var libs = repos.Where(r => r.Name.StartsWith("SpawnDev", StringComparison.OrdinalIgnoreCase)).ToList();
var apps = repos.Where(r => !r.Name.StartsWith("SpawnDev", StringComparison.OrdinalIgnoreCase)).ToList();
sb.AppendLine("## SpawnDev Libraries");
Bullets(libs);
sb.AppendLine();
if (apps.Count > 0)
{
    sb.AppendLine("## Apps and projects built with SpawnDev");
    sb.AppendLine("Real applications that use the SpawnDev libraries (and show off what they can do).");
    sb.AppendLine();
    Bullets(apps);
    sb.AppendLine();
}
if (!string.IsNullOrWhiteSpace(crew))
{
    sb.AppendLine(crew.Trim());
    sb.AppendLine();
}
sb.AppendLine("---");   // separator: everything above = concise summary; below = per-repo detail
sb.AppendLine();
foreach (var (name, desc) in repos)
{
    sb.AppendLine($"## {name}");
    if (desc.Length > 0) sb.AppendLine(desc);
    if (excerpts.TryGetValue(name, out var ex) && ex.Length > 0) { sb.AppendLine(); sb.AppendLine(ex); }
    sb.AppendLine();
    sb.AppendLine($"Repo: https://github.com/{owner}/{name} - full README + CHANGELOG available via github_lookup.");
    sb.AppendLine();
}

await File.WriteAllTextAsync(outFile, sb.ToString());
Console.WriteLine($"[index] wrote {outFile} ({sb.Length} chars, {repos.Count} repos, crew={(crew != null ? "yes" : "no")})");

// Bug-repro / test-harness / issue / demo repos aren't libraries or showcase apps. Ends-with matching for
// Test/Demo/Compat/Throws (so "SpawnDev.UnitTesting" - ending "Testing" - is KEPT), plus Issue/Bug/Repro anywhere.
static bool IsNoise(string name)
    => System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)(Test|Demo|Compat|Throws)$")
       || name.Contains("Issue", StringComparison.OrdinalIgnoreCase)
       || name.Contains("Bug", StringComparison.OrdinalIgnoreCase)
       || name.Contains("Repro", StringComparison.OrdinalIgnoreCase);

// Strip a README's title + badge/HTML noise and return the first prose up to maxChars.
static string Excerpt(string readme, int maxChars)
{
    var keep = new StringBuilder();
    foreach (var raw in readme.Split('\n'))
    {
        var line = raw.TrimEnd('\r');
        var t = line.TrimStart();
        if (t.Length == 0) { if (keep.Length > 0) keep.Append(' '); continue; }
        if (t.StartsWith('#') || t.StartsWith("[![") || t.StartsWith("![") || t.StartsWith('<') || t.StartsWith("---") || t.StartsWith("|")) continue;
        keep.Append(t).Append(' ');
        if (keep.Length >= maxChars) break;
    }
    var s = keep.ToString().Trim();
    return s.Length <= maxChars ? s : s[..maxChars] + "...";
}

static string? ExtractSection(string markdown, string heading)
{
    int i = markdown.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
    if (i < 0) return null;
    int end = markdown.IndexOf("\n## ", i + heading.Length, StringComparison.Ordinal);
    return end < 0 ? markdown[i..] : markdown[i..end];
}
