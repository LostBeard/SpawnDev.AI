using SpawnDev.Reachy;
using SpawnDev.SpawnJS;

namespace SpawnDev.AI.Demo.Reachy;

/// <summary>
/// Drives a Reachy Mini over WebRTC through the Hugging Face signalling Space, presenting the same
/// motion surface as the LAN daemon client so <see cref="ReachyBody"/>'s choreography runs unchanged.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole point of <see cref="IReachyMotion"/>: the gesture classifier, the measured motion
/// envelope and the idle life stay in one place and one implementation. A second copy of the
/// choreography written against WebRTC would eventually disagree with the on-screen body about what a
/// character just did.
/// </para>
/// </remarks>
public sealed class ReachyWebRtcTransport : IReachyMotion, IReachyLifecycle, IAsyncDisposable
{
    private readonly ReachyMiniJs _js;

    /// <summary>Wrap an already-connected SDK client.</summary>
    public ReachyWebRtcTransport(ReachyMiniJs js) => _js = js;

    /// <summary>
    /// Which flat-4x4 layout the daemon expects for the head pose's translation.
    /// </summary>
    /// <remarks>
    /// ✅ MEASURED on a real Reachy Mini (wireless, daemon v1.10.0) 2026-09-16 via
    /// <see cref="VerifyHeadMatrixConventionAsync"/>, over WebRTC from the Hugging Face Space:
    /// <code>
    ///   commanded Z=0.0200 | [11]=0.0191 [14]=0.0000 -> ROW-MAJOR (translation at 3/7/11)
    /// </code>
    /// The head visibly lifted and returned. The 0.0009 shortfall is the servo's reported position
    /// against the commanded one, which is the point: a WRONG layout reads 0.0000 in both slots.
    /// <para>
    /// ⚠️ It had to be measured because the SDK documents <c>head</c> as <c>number[16]</c> (flat 4x4) and
    /// never says row- or column-major, and getting it wrong does NOT throw - the daemon CLAMPS SILENTLY,
    /// so a nonsense pose returns success and simply does not go there. The symptom would have been
    /// "gestures look half-finished", which is indistinguishable from a tuning problem by eye. Re-run the
    /// check rather than trusting this line if the SDK or the daemon changes.
    /// </para>
    /// </remarks>
    public static bool HeadMatrixIsRowMajor { get; set; } = true;

    /// <summary>
    /// Command a known head lift, read the daemon's own reported pose back, and report whether the
    /// translation survived the round trip.
    /// </summary>
    /// <remarks>
    /// ⭐ This exists because the alternative is believing a comment. The robot reports <c>head</c> in the
    /// same wire shape it accepts, so commanding a distinctive Z and reading it back distinguishes the two
    /// layouts in one move. Run it once against hardware and pin the result.
    /// </remarks>
    /// <param name="probeZ">A lift big enough to read clearly but inside the envelope, in metres.</param>
    /// <returns>The measured Z under each layout, and which one matched.</returns>
    public async Task<string> VerifyHeadMatrixConventionAsync(double probeZ = 0.02, CancellationToken ct = default)
    {
        await GotoAsync(headPose: new XyzRpyPose(Z: probeZ), duration: 0.6, ct: ct).ConfigureAwait(false);
        await Task.Delay(900, ct).ConfigureAwait(false);
        _js.RequestState();
        await Task.Delay(700, ct).ConfigureAwait(false);

        var m = _js.HeadMatrix;
        if (m is not { Length: 16 })
            return "INCONCLUSIVE: the daemon has not reported a head matrix yet.";

        // Read the translation out of BOTH candidate layouts; only one can be near probeZ.
        var rowMajorZ = m[11];
        var colMajorZ = m[14];
        var rowHit = Math.Abs(rowMajorZ - probeZ) < probeZ * 0.5;
        var colHit = Math.Abs(colMajorZ - probeZ) < probeZ * 0.5;

        var verdict = (rowHit, colHit) switch
        {
            (true, false) => "ROW-MAJOR (translation at 3/7/11) - the current default is correct.",
            (false, true) => "COLUMN-MAJOR (translation at 12/13/14) - set HeadMatrixIsRowMajor = false.",
            (true, true) => "AMBIGUOUS: both slots match; pick a probeZ that cannot alias.",
            _ => "NEITHER slot matched - the head may have been clamped, or the pose is not a raw 4x4."
        };
        return $"commanded Z={probeZ:F4} | [11]={rowMajorZ:F4} [14]={colMajorZ:F4} -> {verdict}";
    }

    /// <inheritdoc/>
    public Task<MoveHandle?> GotoAsync(
        double? bodyYaw = null,
        XyzRpyPose? headPose = null,
        (double Left, double Right)? antennas = null,
        double duration = 1.0,
        Interpolation interpolation = Interpolation.MinJerk,
        CancellationToken ct = default)
    {
        // ⚠️ The SDK has no interpolation parameter - the daemon picks its own easing for gotoTarget. The
        // argument is accepted and ignored rather than dropped from the interface, because ReachyBody's
        // gestures are tuned WITH it on the LAN path and silently changing that signature would make the
        // two transports look interchangeable when their timing is not.
        var target = new ReachyGotoTarget
        {
            Duration = duration,
            Head = headPose is { } h ? HeadMatrix(h) : null,
            // Wire order is [right, left]; ReachyBody names them (Left, Right). Getting this backwards is
            // a mirrored gesture that still "works", so it is spelled out rather than positional.
            Antennas = antennas is { } a ? [a.Right, a.Left] : null,
            BodyYaw = bodyYaw,
        };

        var ok = _js.GotoTarget(target);
        return Task.FromResult<MoveHandle?>(ok ? null : null);
    }

    /// <summary>
    /// Build the flat 4x4 the daemon expects from an XYZ+RPY pose. Rotation is ZYX
    /// (<c>Rz(yaw) * Ry(pitch) * Rx(roll)</c>), matching the SDK's own <c>rpyToMatrix</c>.
    /// </summary>
    internal static double[] HeadMatrix(XyzRpyPose p)
    {
        double cr = Math.Cos(p.Roll), sr = Math.Sin(p.Roll);
        double cp = Math.Cos(p.Pitch), sp = Math.Sin(p.Pitch);
        double cy = Math.Cos(p.Yaw), sy = Math.Sin(p.Yaw);

        // R = Rz(yaw) * Ry(pitch) * Rx(roll)
        double m00 = cy * cp, m01 = cy * sp * sr - sy * cr, m02 = cy * sp * cr + sy * sr;
        double m10 = sy * cp, m11 = sy * sp * sr + cy * cr, m12 = sy * sp * cr - cy * sr;
        double m20 = -sp,     m21 = cp * sr,                m22 = cp * cr;

        return HeadMatrixIsRowMajor
            ? new[] { m00, m01, m02, p.X,
                      m10, m11, m12, p.Y,
                      m20, m21, m22, p.Z,
                      0,   0,   0,   1 }
            : new[] { m00, m10, m20, 0,
                      m01, m11, m21, 0,
                      m02, m12, m22, 0,
                      p.X, p.Y, p.Z, 1 };
    }

    /// <inheritdoc/>
    public Task SetMotorModeAsync(MotorMode mode, CancellationToken ct = default)
    {
        _js.SetMotorMode(mode switch
        {
            MotorMode.Enabled => "enabled",
            MotorMode.Disabled => "disabled",
            _ => "gravity_compensation"
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<MoveHandle?> WakeUpAsync(CancellationToken ct = default)
    {
        await _js.WakeUpAsync().ConfigureAwait(false);
        return null;
    }

    /// <inheritdoc/>
    public async Task<MoveHandle?> GoHomeAsync(double duration = 1.0, CancellationToken ct = default)
    {
        // Home is the neutral pose: identity rotation, no lift, antennas down, body square.
        await GotoAsync(bodyYaw: 0, headPose: new XyzRpyPose(), antennas: (0, 0),
            duration: duration, ct: ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// End the session and close signalling, so the robot is free for the next connect.
    /// </summary>
    /// <remarks>
    /// ⚠️ Parking the robot is NOT the same as releasing it. Parking is about the hardware - head down,
    /// motors off - while the session is a claim held on the signalling server. Do both, or the next
    /// connect from this page sees no free robot.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        try { await _js.StopSessionAsync().ConfigureAwait(false); }
        catch (Exception ex) { Console.WriteLine($"[reachy] stopSession failed: {ex.Message}"); }
        try { _js.Disconnect(); }
        catch (Exception ex) { Console.WriteLine($"[reachy] disconnect failed: {ex.Message}"); }
    }

    /// <inheritdoc/>
    public async Task<MoveHandle?> GotoSleepAsync(CancellationToken ct = default)
    {
        await _js.GotoSleepAsync().ConfigureAwait(false);
        return null;
    }
}
