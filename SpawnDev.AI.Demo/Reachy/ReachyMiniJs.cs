using SpawnDev.SpawnJS;

namespace SpawnDev.AI.Demo.Reachy;

/// <summary>
/// Typed wrapper over the <c>ReachyMini</c> class from <c>@pollen-robotics/reachy-mini-sdk</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHY THE JS SDK AND NOT OUR OWN SIGNALLING. The robot's daemon speaks plain HTTP on the LAN, so a
/// page served over HTTPS cannot reach it at all - the browser blocks it as mixed content. The supported
/// way to drive a robot from a hosted page is WebRTC, signalled through a central Hugging Face Space
/// which also validates the HF OAuth token and lists the robots on your account. Pollen document the JS
/// <b>API</b> but not the signalling <b>wire protocol</b>, so reimplementing it would mean
/// reverse-engineering an undocumented protocol against a server we do not control and that can change
/// server-side. Wrapping is the supported path, and the one their own apps and desktop app use.
/// </para>
/// <para>
/// ⚠️ Pinned to <b>1.8.0</b> - the docs name that as the release validated against the host shell and the
/// daemon. Do not float the version: the host shell and the daemon are pinned against it too.
/// </para>
/// <para>
/// ⚠️ <b>Wireless robots only.</b> The central signalling server does not serve Reachy Mini Lite, and the
/// robot must be signed in to Hugging Face and registered to the same account as the viewer.
/// </para>
/// <para>
/// ⚠️ This belongs in a SpawnJS RCL (a <c>SpawnDev.SpawnJS.ReachyMini</c> package) rather than in the
/// demo. It is written as a self-contained wrapper with no demo types in its surface so lifting it out is
/// a file move, but it has NOT been lifted yet.
/// </para>
/// </remarks>
public class ReachyMiniJs : SpawnJSObject
{
    /// <summary>Deserialization constructor.</summary>
    public ReachyMiniJs(SpawnJSObjectReference _ref) : base(_ref) { }

    #region Properties

    /// <summary><c>"disconnected"</c>, <c>"connected"</c> or <c>"streaming"</c>.</summary>
    public string State => JSRef!.Get<string>("state");

    /// <summary>True once a valid Hugging Face token is available.</summary>
    public bool IsAuthenticated => JSRef!.Get<bool>("isAuthenticated");

    /// <summary>
    /// True when the page is running inside the host shell's iframe, which preselected a robot for us.
    /// </summary>
    public bool IsEmbedded => JSRef!.Get<bool>("isEmbedded");

    /// <summary>The Hugging Face username, once authenticated.</summary>
    public string? Username => JSRef!.Get<string?>("username");

    /// <summary>True when the robot offers bidirectional audio.</summary>
    public bool MicSupported => JSRef!.Get<bool>("micSupported");

    #endregion

    #region Session lifecycle

    /// <summary>Check for an existing Hugging Face OAuth token.</summary>
    public Task<bool> AuthenticateAsync() => JSRef!.CallAsync<bool>("authenticate");

    /// <summary>Redirect to the Hugging Face login page.</summary>
    public void Login() => JSRef!.CallVoid("login");

    /// <summary>
    /// One-shot bring-up: auth, signalling connect, robot pick, session, wake. Auto-picks when exactly one
    /// robot is free. This is the documented entry point when NOT using the host shell.
    /// </summary>
    public Task AutoConnectAsync() => JSRef!.CallVoidAsync("autoConnect");

    /// <summary>
    /// End the WebRTC session, returning to <c>connected</c>. The robot becomes free again.
    /// </summary>
    /// <remarks>
    /// 🔴 NOT OPTIONAL ON TEARDOWN. The signalling server tracks which robots are busy, and
    /// <c>autoConnect</c> only auto-picks a robot that is FREE. Abandoning a client without stopping its
    /// session leaves the robot held by a session nobody is using, and the next connect attempt in the
    /// same page reports "No reachable robots" - which reads like the robot went offline. MEASURED
    /// 2026-09-16: park and disconnect, then reconnect, failed exactly that way until this was called;
    /// a page refresh "fixed" it only because that discards the whole SDK instance.
    /// </remarks>
    public Task StopSessionAsync() => JSRef!.CallVoidAsync("stopSession");

    /// <summary>Close the signalling connection. Keeps the Hugging Face sign-in.</summary>
    public void Disconnect() => JSRef!.CallVoid("disconnect");

    #endregion

    #region Motion

    /// <summary>
    /// Smooth daemon-side interpolation to a target pose. <paramref name="target"/> carries the raw wire
    /// units: <c>head</c> as a flat 4x4, <c>antennas</c> as <c>[rightRad, leftRad]</c>, <c>body_yaw</c> in
    /// radians, and <c>duration</c> in seconds.
    /// </summary>
    public bool GotoTarget(ReachyGotoTarget target) => JSRef!.Call<ReachyGotoTarget, bool>("gotoTarget", target);

    /// <summary>Set head orientation in DEGREES. Rotation only - no head lift.</summary>
    public bool SetHeadRpyDeg(double roll, double pitch, double yaw)
        => JSRef!.Call<double, double, double, bool>("setHeadRpyDeg", roll, pitch, yaw);

    /// <summary>Set antenna positions in DEGREES, right then left.</summary>
    public bool SetAntennasDeg(double right, double left)
        => JSRef!.Call<double, double, bool>("setAntennasDeg", right, left);

    /// <summary>Set body yaw in DEGREES.</summary>
    public bool SetBodyYawDeg(double yaw) => JSRef!.Call<double, bool>("setBodyYawDeg", yaw);

    #endregion

    #region Power and posture

    /// <summary><c>"enabled"</c>, <c>"disabled"</c> or <c>"gravity_compensation"</c>.</summary>
    public bool SetMotorMode(string mode) => JSRef!.Call<string, bool>("setMotorMode", mode);

    /// <summary>Play the wake-up trajectory; enables motors first. Resolves on daemon completion.</summary>
    public Task WakeUpAsync() => JSRef!.CallVoidAsync("wakeUp");

    /// <summary>Play the sleep trajectory. Resolves on daemon completion.</summary>
    public Task GotoSleepAsync() => JSRef!.CallVoidAsync("gotoSleep");

    /// <summary>Idempotent bring-up to position control. Never rejects.</summary>
    public Task<bool> EnsureAwakeAsync() => JSRef!.CallAsync<bool>("ensureAwake");

    /// <summary>Awake state derived from the cached motor mode.</summary>
    public bool IsAwake() => JSRef!.Call<bool>("isAwake");

    #endregion

    #region Telemetry

    /// <summary>
    /// Ask the daemon for a state snapshot. The answer arrives as a <c>state</c> event, not as a return
    /// value - <see cref="HeadMatrix"/> reads the cached result afterwards.
    /// </summary>
    public bool RequestState() => JSRef!.Call<bool>("requestState");

    /// <summary>
    /// The head pose from the last <c>state</c> event as the flat 4x4 the daemon reports, or null before
    /// the first one arrives. This is what makes the translation convention TESTABLE rather than assumed.
    /// </summary>
    public double[]? HeadMatrix
    {
        get
        {
            using var s = JSRef!.Get<SpawnJSObject?>("robotState");
            return s?.JSRef!.Get<double[]?>("head");
        }
    }

    #endregion

    /// <summary>
    /// Import the SDK module and construct a client against the central signalling server.
    /// </summary>
    /// <remarks>
    /// ⚠️ Loaded as an ES module through <c>JS.Import</c>, which is why this file contains no JavaScript.
    /// </remarks>
    public static async Task<ReachyMiniJs> CreateAsync(SpawnJSRuntime js, string appName, string? clientId)
    {
        // Import assigns the module namespace to a global, so the constructor is reachable by a dotted
        // name - the same shape as JS.New("Intl.DateTimeFormat", ...). No JavaScript is written here.
        await js.Import(SdkGlobal, SdkModuleUrl).ConfigureAwait(false);
        var options = new ReachyMiniOptions { AppName = appName, ClientId = clientId };
        return js.New<ReachyMiniOptions, ReachyMiniJs>($"{SdkGlobal}.ReachyMini", options);
    }

    /// <summary>Global the imported module namespace is assigned to.</summary>
    private const string SdkGlobal = "reachyMiniSdk";

    /// <summary>
    /// The pinned SDK, served as an ES module. jsDelivr's <c>/+esm</c> endpoint rewrites the package's
    /// bare imports into a single browser-loadable module.
    /// </summary>
    public const string SdkModuleUrl =
        "https://cdn.jsdelivr.net/npm/@pollen-robotics/reachy-mini-sdk@1.8.0/+esm";
}
