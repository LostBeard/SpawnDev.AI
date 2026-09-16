// Publish the demo and deploy it to the Hugging Face Space.
//
//   dotnet run tools/deploy-space.cs              from the repo root
//   right-click -> "dotnet run script"            from anywhere, no arguments
//
// 🔴 WHY A SCRIPT. The Space is where Reachy actually works - a page served over HTTPS cannot reach the
// robot's daemon on the LAN (mixed content), so the WebRTC path through the Hugging Face signalling
// server is the only route from a hosted page, and that route only exists on the Space. Redeploying by
// hand was publish -> clone -> wipe -> copy -> commit -> push, and two of those steps have a trap in them
// (below). A five-step manual ritual run repeatedly is a defect waiting for the one time it is done tired.
//
// Flags (all optional): --dry-run  build and stage, do not push
//                       --no-pause don't wait for a keypress at the end
using System.Diagnostics;
using System.Runtime.CompilerServices;

var dryRun = args.Contains("--dry-run");
var pause = !args.Contains("--no-pause") && !Console.IsInputRedirected;

const string SpaceId = "LostBeard/spawndev-ai";
const string SpaceGit = $"https://huggingface.co/spaces/{SpaceId}";
const string SpaceUrl = "https://lostbeard-spawndev-ai.static.hf.space/";

var exit = 0;
try
{
    exit = await Run();
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"FAILED: {ex.Message}");
    exit = 1;
}
if (pause)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to close...");
    try { Console.ReadKey(true); } catch { /* no console to read from */ }
}
return exit;

async Task<int> Run()
{
    var repo = RepoRoot();
    Console.WriteLine($"repo   : {repo}");
    Console.WriteLine($"space  : {SpaceId}");
    if (dryRun) Console.WriteLine("mode   : DRY RUN - nothing will be pushed");
    Console.WriteLine();

    var demo = Path.Combine(repo, "SpawnDev.AI.Demo");
    if (!Directory.Exists(demo)) throw new Exception($"No SpawnDev.AI.Demo under {repo}.");

    // ── 1. Publish ──────────────────────────────────────────────────────────────────────────────────
    // 🔴 PUBLISH, never build. Blazor WASM is only relinked and wasm-opt'd on publish; a build output is
    // a different, slower app. Measured elsewhere in this repo at 2x+ on real timings.
    var pub = Path.Combine(Path.GetTempPath(), "spawndev-ai-space-publish");
    if (Directory.Exists(pub)) Directory.Delete(pub, true);
    Console.WriteLine("[1/5] publishing (relink + wasm-opt, this is the slow one)...");
    Sh("dotnet", $"publish \"{demo}\" --nologo -c Release --output \"{pub}\"", repo);

    var wwwroot = Path.Combine(pub, "wwwroot");
    if (!Directory.Exists(wwwroot)) throw new Exception($"Publish produced no wwwroot at {pub}.");
    var published = Directory.GetFiles(wwwroot, "*", SearchOption.AllDirectories).Length;
    Console.WriteLine($"      {published} files published");

    // ── 2. Get the Space repo ───────────────────────────────────────────────────────────────────────
    var work = Path.Combine(Path.GetTempPath(), "spawndev-ai-space-repo");
    if (Directory.Exists(Path.Combine(work, ".git")))
    {
        Console.WriteLine("[2/5] refreshing the Space clone...");
        Sh("git", "fetch --quiet origin", work);
        Sh("git", "reset --hard origin/main --quiet", work);
    }
    else
    {
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Console.WriteLine("[2/5] cloning the Space...");
        Sh("git", $"clone --quiet {SpaceGit} \"{work}\"", Path.GetTempPath());
    }

    // ── 3. Replace the content ──────────────────────────────────────────────────────────────────────
    // ⚠️ .git and .gitattributes SURVIVE. Wiping .gitattributes is not cosmetic: Hugging Face refuses a
    // push whose binaries are not in LFS, and that file is what puts them there. Deleting it once already
    // cost two rejected pushes.
    Console.WriteLine("[3/5] replacing Space content...");
    foreach (var d in Directory.GetDirectories(work))
        if (Path.GetFileName(d) is not ".git") Directory.Delete(d, true);
    foreach (var f in Directory.GetFiles(work))
        if (Path.GetFileName(f) is not ".gitattributes") File.Delete(f);
    CopyDir(wwwroot, work);

    // 🔴 THE SPACE'S OWN FILES ARE VERSIONED HERE, NOT ONLY IN THE SPACE. README.md carries the YAML
    // frontmatter, and `hf_oauth: true` in it is what makes Hugging Face inject OAUTH_CLIENT_ID into the
    // page - which is the ONLY reason "Connect my Reachy" can sign anyone in. Publishing over the Space
    // without restoring it would silently break robot sign-in while everything else kept working.
    var spaceFiles = Path.Combine(repo, "tools", "space");
    foreach (var name in new[] { "README.md", ".gitattributes" })
    {
        var src = Path.Combine(spaceFiles, name);
        if (File.Exists(src)) File.Copy(src, Path.Combine(work, name), overwrite: true);
        else Console.WriteLine($"      ⚠️ tools/space/{name} is missing - keeping whatever the Space had");
    }

    var readme = File.Exists(Path.Combine(work, "README.md")) ? File.ReadAllText(Path.Combine(work, "README.md")) : "";
    if (!readme.Contains("hf_oauth: true"))
        throw new Exception("The Space README has no 'hf_oauth: true' - robot sign-in would break. Refusing to deploy.");
    if (!File.Exists(Path.Combine(work, "index.html")))
        throw new Exception("No index.html in the staged Space - the publish output looks wrong.");

    // ── 4. Commit ───────────────────────────────────────────────────────────────────────────────────
    Console.WriteLine("[4/5] committing...");
    Sh("git", "add -A", work);
    var staged = ShOut("git", "diff --cached --name-only", work).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    if (staged == 0)
    {
        Console.WriteLine("      nothing changed - the Space already matches this build.");
        return 0;
    }
    Console.WriteLine($"      {staged} files changed");
    var msg = $"Deploy {DateTime.Now:yyyy-MM-dd HH:mm} - {published} files";
    Sh("git", $"-c user.email=lostit1278@gmail.com -c user.name=LostBeard commit --quiet -m \"{msg}\"", work);

    if (dryRun)
    {
        Console.WriteLine();
        Console.WriteLine($"DRY RUN - staged in {work}, not pushed.");
        return 0;
    }

    // ── 5. Push ─────────────────────────────────────────────────────────────────────────────────────
    // The token comes from `hf auth login` and is fed to git through a one-shot credential helper, so it
    // never lands in .git/config and is never printed.
    Console.WriteLine("[5/5] pushing (LFS upload, ~57 MB)...");
    var token = HuggingFaceToken();
    if (token.Length == 0)
        throw new Exception("No Hugging Face token. Run:  hf auth login");

    var helper = "!f() { echo username=LostBeard; echo \"password=$HF_PW\"; }; f";
    Sh("git", $"-c credential.helper=\"{helper}\" push origin main", work, ("HF_PW", token));

    // ── Verify, rather than assume ──────────────────────────────────────────────────────────────────
    // ⚠️ A green push is not a working page. This checks the Space actually serves the app again, and
    // that the OAuth id Hugging Face injects is still there - the two things a bad deploy silently loses.
    Console.WriteLine();
    Console.WriteLine("verifying...");
    await Task.Delay(8000);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    try
    {
        var html = await http.GetStringAsync(SpaceUrl);
        var ok = html.Contains("<title>");
        var oauth = html.Contains("OAUTH_CLIENT_ID");
        Console.WriteLine($"  page served     : {(ok ? "yes" : "NO")}");
        Console.WriteLine($"  OAUTH_CLIENT_ID : {(oauth ? "injected" : "MISSING - robot sign-in will fail")}");
        if (!ok || !oauth)
        {
            Console.WriteLine("  (the Space may still be rebuilding - re-check in a minute before panicking)");
            return 2;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  could not fetch the Space yet: {ex.Message}");
        Console.WriteLine("  (it may still be rebuilding)");
        return 2;
    }

    Console.WriteLine();
    Console.WriteLine($"DEPLOYED -> {SpaceUrl}");
    return 0;
}

/// <summary>
/// The repo root, found from THIS FILE's own location.
/// </summary>
/// <remarks>
/// ⚠️ Not from AppContext.BaseDirectory and not from the working directory. A single-file `dotnet run`
/// builds into %TEMP%\dotnet\..., so anything relative to the assembly lands nowhere near the repo; and
/// right-clicking the script in Explorer sets the working directory to wherever Explorer feels like.
/// [CallerFilePath] is filled in by the compiler with this source file's real path, which is the one
/// thing that is true however it was launched.
/// </remarks>
static string RepoRoot([CallerFilePath] string thisFile = "")
{
    var dir = Path.GetDirectoryName(Path.GetFullPath(thisFile))!;   // .../SpawnDev.AI/tools
    var root = Path.GetDirectoryName(dir)!;                         // .../SpawnDev.AI
    if (Directory.Exists(Path.Combine(root, "SpawnDev.AI.Demo"))) return root;

    // Fall back to walking up from the working directory, for an unusual launcher.
    var cur = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (cur != null)
    {
        if (Directory.Exists(Path.Combine(cur.FullName, "SpawnDev.AI.Demo"))) return cur.FullName;
        cur = cur.Parent;
    }
    throw new Exception($"Could not find the repo root. Looked beside {thisFile} and above {Directory.GetCurrentDirectory()}.");
}

static void CopyDir(string from, string to)
{
    foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
    foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
}

static void Sh(string exe, string cmdArgs, string cwd, params (string Key, string Value)[] env)
{
    var psi = new ProcessStartInfo(exe, cmdArgs) { WorkingDirectory = cwd, UseShellExecute = false };
    foreach (var (k, v) in env) psi.Environment[k] = v;
    using var p = Process.Start(psi) ?? throw new Exception($"Could not start {exe}.");
    p.WaitForExit();
    // ⚠️ The message deliberately does NOT include the arguments: one of these calls carries the
    // Hugging Face token in an environment variable, and a failure dump is exactly where a secret leaks.
    if (p.ExitCode != 0) throw new Exception($"{exe} exited {p.ExitCode}.");
}

/// <summary>
/// The Hugging Face token, from the environment or the file that <c>hf auth login</c> writes.
/// </summary>
/// <remarks>
/// ⚠️ Read from DISK, not by running <c>hf auth token</c>. On Windows the CLI is <c>hf.exe</c>, and
/// <c>Process.Start</c> cannot resolve the bare name <c>hf</c> the way a shell can - which failed here as
/// a confident "No Hugging Face token. Run: hf auth login" while the user was in fact logged in. Reading
/// the file the CLI itself writes has no such ambiguity and no process to spawn.
/// </remarks>
static string HuggingFaceToken()
{
    var env = Environment.GetEnvironmentVariable("HF_TOKEN");
    if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

    var candidates = new List<string>();
    var home = Environment.GetEnvironmentVariable("HF_HOME");
    if (!string.IsNullOrWhiteSpace(home)) candidates.Add(Path.Combine(home, "token"));
    candidates.Add(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "huggingface", "token"));

    foreach (var c in candidates)
        if (File.Exists(c))
        {
            var t = File.ReadAllText(c).Trim();
            if (t.Length > 0) return t;
        }
    return "";
}

static string ShOut(string exe, string cmdArgs, string cwd)
{
    var psi = new ProcessStartInfo(exe, cmdArgs)
    {
        WorkingDirectory = cwd,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    using var p = Process.Start(psi) ?? throw new Exception($"Could not start {exe}.");
    var stdout = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    return stdout;
}
