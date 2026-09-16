using SpawnDev.Reachy.Browser;
using SpawnDev.Reachy;
using SpawnDev.SpawnJS;

namespace SpawnDev.AI.Demo;

/// <summary>
/// Drives the physical Reachy Mini for whichever character holds it.
/// </summary>
/// <remarks>
/// <para>
/// A thin adapter, on purpose. The classifying and the choreography both live in <c>SpawnDev.Reachy</c>
/// (<c>SpokenText</c>, <c>GestureClassifier</c>, <c>ReachyBody</c>), which were promoted out of the Rose
/// app precisely so another host could do this without reimplementing them. Everything here is
/// connection lifecycle and policy.
/// </para>
/// <para>
/// 🔴 THE ROBOT SPEAKS PLAIN HTTP ON THE LAN. A page served over HTTPS cannot reach it at all - the
/// browser blocks it as mixed content, and the failure surfaces as an opaque network error rather than
/// anything mentioning security. That is why there are TWO transports:
/// <list type="bullet">
/// <item><see cref="ConnectAsync"/> - plain HTTP to the daemon. Only from a local page.</item>
/// <item><see cref="ConnectWebRtcAsync"/> - WebRTC, signalled through a Hugging Face Space, which works
/// from any HTTPS page and reaches whichever robots the signed-in account owns.</item>
/// </list>
/// The choreography does not know the difference: both satisfy <c>IReachyMotion</c>, so
/// <c>ReachyBody</c>'s gestures are written once.
/// </para>
/// </remarks>
public sealed class ReachyDriver : IAsyncDisposable
{
    private IReachyLifecycle? _lifecycle;
    private IDisposable? _owned;      // the LAN client, when we made one. The SDK client is not IDisposable.
    private ReachyBody? _body;
    private ReachyPresence? _presence;

    /// <summary>How the robot is reached.</summary>
    public enum Link
    {
        /// <summary>No connection.</summary>
        None,
        /// <summary>Plain HTTP to the daemon on the LAN. Local pages only.</summary>
        LanDaemon,
        /// <summary>WebRTC, signalled through the Hugging Face Space. Works from any HTTPS page.</summary>
        WebRtc,
    }

    /// <summary>Which transport is live.</summary>
    public Link Transport { get; private set; } = Link.None;

    /// <summary>
    /// The robot's speaker, when one is connected over WebRTC. Null otherwise.
    /// </summary>
    /// <remarks>
    /// A character holding the robot speaks through THIS instead of the page's audio output - that is
    /// most of what "having a body" means to a listener, more than the gestures.
    /// </remarks>
    public ReachySpeaker? Speaker { get; private set; }

    /// <summary>
    /// The robot's own microphone array, once connected over WebRTC.
    /// </summary>
    /// <remarks>
    /// The stream is handed over raw. Turning it into speech is the app's existing capture pipeline's job
    /// (<c>MediaStreamCapture.StartFromAudioStreamAsync</c>), which is the same one the browser microphone
    /// goes through - so a character listening through the robot uses the identical detector and
    /// recogniser rather than a second path that can drift.
    /// </remarks>
    public ReachyEars? Ears { get; private set; }

    /// <summary>The robot's address, as last connected.</summary>
    public string Address { get; private set; } = "";

    /// <summary>True when there is a live client to command.</summary>
    public bool IsConnected => _body != null;

    /// <summary>Last thing that happened, for the UI to show.</summary>
    public string Status { get; private set; } = "Not connected.";

    /// <summary>
    /// Why connecting cannot work from this page, or null when it can.
    /// </summary>
    /// <param name="pageOrigin">The page's own origin, e.g. from <c>location.origin</c>.</param>
    /// <remarks>
    /// ⚠️ Checked and reported UP FRONT. A browser's mixed-content block looks exactly like an unreachable
    /// robot, so without this the user would be checking cables for a problem that is the page's scheme.
    /// </remarks>
    public static string? MixedContentWarning(string? pageOrigin)
        => pageOrigin != null && pageOrigin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "This page is served over HTTPS, and the robot's daemon speaks plain HTTP on your LAN, so the "
            + "browser blocks that connection as mixed content. Connect over WebRTC instead - it signs in "
            + "with Hugging Face and reaches your robot through the signalling server."
            : null;

    /// <summary>
    /// Connect over WebRTC, signalled through the Hugging Face Space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the path that works from a hosted page. <c>autoConnect</c> does the whole bring-up -
    /// Hugging Face sign-in, signalling connect, robot pick, WebRTC session, wake - and auto-picks when
    /// exactly one robot on the account is free.
    /// </para>
    /// <para>
    /// ⚠️ Wireless robots only, and the robot must be signed in to Hugging Face under the SAME account as
    /// whoever is looking at the page. Someone else's robot is not listed and cannot be reached; that is
    /// the signalling server's rule, not ours.
    /// </para>
    /// <para>
    /// ✅ VERIFIED END TO END on a real wireless Reachy Mini, 2026-09-16: Hugging Face sign-in,
    /// signalling, robot pick, WebRTC session, wake, and commanded head motion arriving correctly
    /// (see <see cref="ReachyWebRtcTransport.HeadMatrixIsRowMajor"/> for the pose-format measurement).
    /// </para>
    /// </remarks>
    public async Task<bool> ConnectWebRtcAsync(SpawnJSRuntime js, CancellationToken ct = default)
    {
        await DisconnectAsync(ct).ConfigureAwait(false);
        try
        {
            // 🔴 THE CLIENT ID IS THE SPACE'S, NOT A NAME WE INVENT. A Hugging Face Space with
            // `hf_oauth: true` has its OAuth app registered for us and injects the id into the page as
            // window.huggingface.variables.OAUTH_CLIENT_ID. Passing an arbitrary string instead means the
            // SDK has no registered client, and the failure reads as
            // "Not authenticated - call login() or pass a token" rather than as a bad client id.
            var clientId = js.Get<string?>("huggingface.variables.OAUTH_CLIENT_ID");
            var sdk = await ReachyMiniJs.CreateAsync(js, AppName, clientId).ConfigureAwait(false);

            // 🔴 EARS BEFORE CONNECT, NOT AFTER. The SDK emits its media track exactly once, during the
            // connect handshake, and has no accessor for the current stream - so a listener attached after
            // AutoConnectAsync resolves hears nothing at all, on a robot that is working perfectly and
            // with no error anywhere. This is the one ordering in this method that cannot be rearranged.
            Ears = new ReachyEars(sdk);
            Ears.Log += m => Console.WriteLine(m);

            // 🔴 autoConnect FIRST, login() only as the fallback. The docs describe autoConnect as the
            // whole chain - auth, signalling, robot pick, session, wake - and "auth" includes completing
            // the Hugging Face redirect, i.e. exchanging the ?code= the browser comes back with for a
            // token. Calling authenticate() in front of it, as this used to, is a check for an EXISTING
            // token only: it answers false on the way back from a sign-in, so the code is never
            // exchanged, login() fires again, and the page redirects FOREVER. Observed: two round trips
            // in a row, each landing on ?code= and immediately leaving again.
            Status = "Signing in to Hugging Face and looking for your robot...";
            try
            {
                await sdk.AutoConnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (NeedsSignIn(ex))
            {
                // Genuinely no token and nothing to exchange: send them to sign in. The page navigates
                // away, and on the way back autoConnect above completes the handshake.
                Status = "Sending you to Hugging Face to sign in - you will come back here.";
                sdk.Login();
                return false;
            }

            var transport = new ReachyWebRtcTransport(sdk);
            // Every command the robot receives, with the gap since the last one. This is what makes
            // "the movement is jerky" a readable timeline instead of an impression.
            transport.Log += m => Console.WriteLine($"[reachy-cmd] {m}");
            // The robot's own speaker, so a character with a body sounds like it is in the room. Only on
            // the WebRTC transport: the LAN daemon path has its own sound API and is not wired to this.
            Speaker = new ReachySpeaker(sdk);
            Speaker.Log += m => Console.WriteLine(m);
            _lifecycle = transport;
            _owned = null;
            _body = new ReachyBody(transport);
            _body.Log += m => Console.WriteLine($"[reachy] {m}");
            StartPresence();
            Transport = Link.WebRtc;
            Address = sdk.Username is { Length: > 0 } u ? $"{u}'s robot (WebRTC)" : "your robot (WebRTC)";
            Status = $"Connected to {Address}.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Could not connect over WebRTC: {ex.Message}";
            return false;
        }
    }

    /// <summary>Name this app advertises to the robot and to Hugging Face.</summary>
    private const string AppName = "SpawnDev.AI";

    /// <summary>
    /// Whether a failure from the SDK means "nobody is signed in" rather than something being broken.
    /// </summary>
    /// <remarks>
    /// ⚠️ Matched on the message because the SDK reports it as a plain error. Getting this wrong in the
    /// permissive direction is a redirect loop, so it matches the SDK's own wording and nothing looser.
    /// </remarks>
    private static bool NeedsSignIn(Exception ex)
        => ex.Message.Contains("Not authenticated", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("call login()", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Move the robot a known amount and read the daemon's own answer back, to settle whether the head
    /// pose's translation is being sent in the layout the daemon expects.
    /// </summary>
    /// <remarks>
    /// 🔴 The SDK documents the head pose as a flat 4x4 and never says row- or column-major. Guessing
    /// wrong does NOT raise anything: the daemon clamps silently, so every gesture still "works" and just
    /// never lifts the head. That is indistinguishable from a tuning problem by eye, which is why this
    /// exists as a button rather than as something to infer from a performance.
    /// </remarks>
    public async Task<string> SelfTestAsync(CancellationToken ct = default)
    {
        if (_lifecycle is not ReachyWebRtcTransport webrtc)
            return "Connect over WebRTC first - this checks the WebRTC pose format specifically.";
        try
        {
            var verdict = await webrtc.VerifyHeadMatrixConventionAsync(ct: ct).ConfigureAwait(false);
            await webrtc.GoHomeAsync(0.8, ct).ConfigureAwait(false);
            Status = verdict;
            return verdict;
        }
        catch (Exception ex)
        {
            Status = $"Self-test failed: {ex.Message}";
            return Status;
        }
    }

    /// <summary>
    /// Tell the robot what the app is doing, so a person in another room can see it.
    /// </summary>
    /// <remarks>
    /// 🔴 THE ROBOT IS THE INTERFACE WHEN IT IS NOT NEXT TO THE SCREEN. "Listening", "thinking" and "idle"
    /// are obvious on a page - a level meter, a spinner, a cursor - and on the robot they were one state:
    /// perfectly still. Someone who has just spoken to a motionless robot cannot tell whether it heard
    /// them, is working, has finished, or is broken, and the natural response to all four is to repeat
    /// themselves.
    ///
    /// ⚠️ Fire-and-forget on purpose. This is decoration on a wireless link, and a turn must never wait on
    /// it - nor fail because of it.
    /// </remarks>
    /// <summary>
    /// Change mood and WAIT for the body to be handed over.
    /// </summary>
    /// <remarks>
    /// 🔴 THE FIRE-AND-FORGET FORM IS NOT ENOUGH BEFORE A PERFORMANCE. Stopping the thinking loop means
    /// waiting out whatever gesture it already had in flight - <c>ReachyBody</c> allows one at a time and
    /// DROPS rather than queues - so a reply's own action issued immediately after <see cref="Mood"/> is
    /// skipped, logged only as "busy, skipped gesture". MEASURED 2026-09-16, and the reason a stage
    /// direction at the start of a reply was not performed.
    /// </remarks>
    public async Task MoodAsync(ReachyMood mood, CancellationToken ct = default)
    {
        if (_presence is not { } presence) return;
        try { await presence.SetAsync(mood, ct).ConfigureAwait(false); }
        catch (Exception ex) { Console.WriteLine($"[reachy-mood] {mood} failed: {ex.Message}"); }
    }

    public void Mood(ReachyMood mood)
    {
        if (_presence is not { } presence) return;
        _ = presence.SetAsync(mood).ContinueWith(
            t => Console.WriteLine($"[reachy-mood] {mood} failed: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Start the idle life the moment a body exists, so the robot is never simply frozen.</summary>
    private void StartPresence()
    {
        if (_body is not { } body) return;
        body.StartIdle(GestureStyle.Default);
        _presence = new ReachyPresence(body);
        _presence.Log += m => Console.WriteLine(m);
    }

    /// <summary>
    /// Play a tone out of the robot's own speaker, and say what happened.
    /// </summary>
    /// <remarks>
    /// ⚠️ The daemon does not validate audio and does not transcode, so a wrong format is SILENCE rather
    /// than an error - which means this check is judged by ear and has to be trivially easy to run. It
    /// deliberately involves no language model and no voice model: reaching the speaker through a real
    /// reply costs minutes and a failure could belong to either of them.
    /// </remarks>
    public async Task<string> SpeakerTestAsync(CancellationToken ct = default)
    {
        if (Speaker is not { } speaker)
            return "Connect over WebRTC first - the robot's speaker is only reachable that way.";
        try
        {
            Status = "Playing a 1-second tone out of the robot...";
            var seconds = await speaker.PlayTestToneAsync(ct: ct).ConfigureAwait(false);
            Status = $"Played a {seconds:F2}s 440 Hz tone through the robot. Did you hear it?";
            return Status;
        }
        catch (Exception ex)
        {
            Status = $"Robot speaker test failed: {ex.Message}";
            return Status;
        }
    }

    /// <summary>
    /// Connect and enable the motors.
    /// </summary>
    /// <remarks>
    /// Motors are enabled here rather than lazily on the first gesture: a gesture sent to a robot with its
    /// motors off does nothing and reports nothing, which reads as a broken classifier.
    /// </remarks>
    public async Task<bool> ConnectAsync(string address, CancellationToken ct = default)
    {
        await DisconnectAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(address))
        {
            Status = "Enter the robot's address first.";
            return false;
        }

        try
        {
            var client = new ReachyMiniClient(address.Trim());
            // A cheap read first: it proves the daemon is actually there before anything is commanded.
            var status = await client.GetStatusAsync(ct).ConfigureAwait(false);
            if (status == null)
            {
                client.Dispose();
                Status = $"No daemon answered at {address}.";
                return false;
            }

            await client.SetMotorModeAsync(MotorMode.Enabled).ConfigureAwait(false);

            // 🔴 WAKE IT BEFORE GESTURING. A parked robot sits with its head lowered into its chest
            // (MEASURED on a real unit at rest: pitch 0.50 rad down), and firing an expressive gesture
            // from there starts it with a lurch out of the park pose. wake_up is the daemon's own move for
            // exactly this transition, and it is the mirror of the parking recipe in DisconnectAsync.
            await client.WakeUpAsync(ct).ConfigureAwait(false);
            await Task.Delay(1500, ct).ConfigureAwait(false);

            _lifecycle = client;
            _owned = client;
            Transport = Link.LanDaemon;
            _body = new ReachyBody(client);
            _body.Log += m => Console.WriteLine($"[reachy] {m}");
            StartPresence();
            Address = address.Trim();
            Status = $"Connected to {Address}, motors enabled.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Could not connect to {address}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Perform one written stage direction on the robot.
    /// </summary>
    /// <param name="actionText">The direction as the model wrote it - NOT a pre-classified gesture.</param>
    /// <param name="motionScale">How animated this character is. 1.0 is normal.</param>
    /// <remarks>
    /// ⚠️ The RAW text goes to the SDK, which classifies it itself. Passing a gesture this side would mean
    /// two classifiers - and the on-screen body and the robot would eventually disagree about what a
    /// character just did.
    /// </remarks>
    public async Task PerformAsync(string actionText, double motionScale = 1.0, CancellationToken ct = default)
    {
        if (_body is not { } body) return;
        try
        {
            await body.PerformAsync(actionText, new GestureStyle((0, 0), motionScale), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A gesture failing must never take down a turn - the character still spoke.
            Status = $"Gesture failed: {ex.Message}";
            Console.WriteLine($"[reachy] gesture '{actionText}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Park the robot properly and drop the connection.
    /// </summary>
    /// <remarks>
    /// 🔴 THE ORDER IS A CONFIRMED RECIPE AND IS NOT NEGOTIABLE: go home FIRST, then sleep, wait for the
    /// head to settle, and only then motors off. <c>goto_sleep</c> starts from wherever the robot IS, and
    /// speaking leaves the head lifted - sleeping from there throws the head back. From home it lowers
    /// into the chest. Motors-off from a head-up pose is not stable.
    /// </remarks>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        var life = _lifecycle;
        var owned = _owned;
        _body = null;
        // Stop animating before the body goes; an in-flight gesture against a disposed transport is an
        // unhandled exception on a background task, which exits the WASM runtime rather than failing.
        if (_presence is { } presence)
        {
            _presence = null;
            try { await presence.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { Console.WriteLine($"[reachy-mood] dispose: {ex.Message}"); }
        }
        _lifecycle = null;
        _owned = null;
        Speaker = null;
        // Every += needs its -=, or the JS callback outlives this object. An unhandled exception on a
        // runtime callback exits the WASM runtime rather than failing a turn.
        try { Ears?.Dispose(); } catch (Exception ex) { Console.WriteLine($"[reachy-ears] dispose: {ex.Message}"); }
        Ears = null;
        Transport = Link.None;
        if (life == null) return;

        try
        {
            await life.GoHomeAsync(1.0, ct).ConfigureAwait(false);
            await Task.Delay(1100, ct).ConfigureAwait(false);
            await life.GotoSleepAsync(ct).ConfigureAwait(false);
            await Task.Delay(1500, ct).ConfigureAwait(false);
            await life.SetMotorModeAsync(MotorMode.Disabled, ct).ConfigureAwait(false);
            Status = "Parked and disconnected.";
        }
        catch (Exception ex)
        {
            Status = $"Disconnected, but parking did not complete: {ex.Message}";
        }
        finally
        {
            // 🔴 RELEASE THE SESSION, not just the hardware. Parking puts the robot to sleep; the
            // signalling server still counts it as claimed until the session is stopped, and the next
            // connect from this same page then finds "no reachable robots".
            if (life is IAsyncDisposable session)
            {
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { Console.WriteLine($"[reachy] session teardown failed: {ex.Message}"); }
            }
            owned?.Dispose();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
