using SpawnDev.Reachy;

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
/// anything mentioning security. So this works from a locally served demo (<c>http://localhost</c> is a
/// secure context) and NOT from the public GitHub Pages build. <see cref="MixedContentWarning"/> says so
/// rather than letting the user debug a connection that cannot succeed.
/// </para>
/// </remarks>
public sealed class ReachyDriver : IAsyncDisposable
{
    private ReachyMiniClient? _client;
    private ReachyBody? _body;

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
            ? "This page is served over HTTPS, and the robot's daemon speaks plain HTTP on your LAN - the "
            + "browser will block the connection as mixed content. Run the demo locally (http://localhost) "
            + "to drive the robot."
            : null;

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

            _client = client;
            _body = new ReachyBody(client);
            _body.Log += m => Console.WriteLine($"[reachy] {m}");
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
        var client = _client;
        _body = null;
        _client = null;
        if (client == null) return;

        try
        {
            await client.GoHomeAsync(1.0, ct).ConfigureAwait(false);
            await Task.Delay(1100, ct).ConfigureAwait(false);
            await client.GotoSleepAsync(ct).ConfigureAwait(false);
            await Task.Delay(1500, ct).ConfigureAwait(false);
            await client.SetMotorModeAsync(MotorMode.Disabled).ConfigureAwait(false);
            Status = "Parked and disconnected.";
        }
        catch (Exception ex)
        {
            Status = $"Disconnected, but parking did not complete: {ex.Message}";
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
