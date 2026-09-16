namespace SpawnDev.AI.Demo.Pages;

/// <summary>
/// The solo assistant's body: it acts out what it writes, the same way a room character does.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE REPORT. Captain: "the avatar seem to be 100% static, never changes. does the ai know to use it?
/// every ai should have and use an avatar by default (unless specifically turned off for that persona)."
/// </para>
/// <para>
/// He was right twice over. The room path had a complete pipeline - written direction -&gt;
/// <c>GestureClassifier</c> -&gt; on-screen body and the physical Reachy - and the solo avatar was rendered
/// with <c>Speaking</c> and NO <c>Action</c> at all, so nothing could ever move it. And nothing in the
/// default system prompt told the model it had a body, so even a working pipeline would have had nothing
/// to perform: an assistant that is never told it can move does not move.
/// </para>
/// <para>
/// ⚠️ DRIVEN FROM THE TURN, NOT FROM SPEECH. The obvious place is the speak path, where the reply is
/// already being split into words and actions - and it is the wrong place. Speaking is optional here (the
/// demo answers in text unless a voice is picked or hands-free is on), so a body wired to the voice is
/// motionless for most users. That is the reported symptom, not a variant of it.
/// </para>
/// </remarks>
public partial class Home
{
    /// <summary>How long each solo gesture is left on screen before the next one.</summary>
    /// <remarks>
    /// The same 1300 ms the room uses for a drawn (non-robot) body, so one assistant does not act at a
    /// visibly different tempo from a character standing next to it.
    /// </remarks>
    const int SoloGestureMs = 1300;

    /// <summary>The motion the solo assistant's body is performing right now.</summary>
    SpawnDev.Reachy.Gesture _soloAction = SpawnDev.Reachy.Gesture.None;

    /// <summary>Cancels an in-flight body performance when a new turn starts.</summary>
    CancellationTokenSource? _soloActionCts;

    /// <summary>
    /// Perform a reply's written actions, in order, on the solo assistant's body.
    /// </summary>
    /// <remarks>
    /// 🔴 FIRE-AND-FORGET, SO IT CATCHES EVERYTHING - the same rule as the room's PlayActionsAsync. This
    /// runs unawaited so the body moves while the reply is read or spoken, and an unhandled exception on an
    /// unawaited task in Blazor WASM does not fail a turn: it EXITS THE RUNTIME and takes the page with it.
    /// A decorative animation must never be able to do that.
    /// </remarks>
    async Task PlaySoloActionsAsync(IReadOnlyList<string> written, CancellationToken ct)
    {
        try
        {
            foreach (var text in written)
            {
                if (ct.IsCancellationRequested) break;
                var gesture = SpawnDev.Reachy.GestureClassifier.Classify(text);
                if (gesture == SpawnDev.Reachy.Gesture.None) continue;

                _soloAction = gesture;
                await InvokeAsync(StateHasChanged);

                // 🔴 AND ON THE ROBOT, IF IT IS THE BODY IN USE. This loop only ever moved the on-screen
                // avatar, so a connected Reachy stood still through "*tilts head*" while the SVG tilted -
                // Captain, watching it: "Reachy did not tilt it's head at the end of saying the sentence
                // like i would have expected". The same rule as the voice decides it (
                // AvatarActions.SpeakerDrivesRobot): a robot nobody in the room has claimed belongs to
                // whoever is talking, and the solo assistant has no agent id at all.
                //
                // ⚠️ AWAITED, not fired off, and it REPLACES the delay rather than running beside it.
                // ReachyBody sequences by waiting out each movement's real duration - the daemon's goto
                // only queues - so a fixed Task.Delay running in parallel would let the next gesture
                // interrupt this one partway through, which is a defect this stack has already paid for.
                if (SpeakingAgentDrivesRobot())
                {
                    try { await Robot.PerformAsync(text, ct: ct); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { Console.WriteLine($"[BODY] robot gesture failed: {ex.Message}"); }
                }
                else
                {
                    try { await Task.Delay(SoloGestureMs, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[BODY] solo animation stopped: {ex.Message}"); }
        finally
        {
            // Always return to rest, or the last motion of the reply stays frozen on the body.
            _soloAction = SpawnDev.Reachy.Gesture.None;
            try { await InvokeAsync(StateHasChanged); } catch { /* the page is going away */ }
        }
    }

    /// <summary>
    /// Act out a finished reply, cancelling whatever the body was still doing from the previous one.
    /// </summary>
    /// <remarks>
    /// ⚠️ Cancelling first matters: two replies in quick succession would otherwise have two loops writing
    /// <see cref="_soloAction"/>, and the older one's "back to rest" in its finally would land AFTER the
    /// newer one's first gesture and freeze the body mid-turn.
    /// </remarks>
    void PerformReply(string? reply)
    {
        var actions = StageDirections.SplitForBody(reply).Actions;
        _soloActionCts?.Cancel();
        _soloActionCts?.Dispose();
        _soloActionCts = null;
        if (actions.Count == 0) return;

        Console.WriteLine($"[BODY] {actions.Count} action(s): {string.Join(", ", actions)}");
        _soloActionCts = new CancellationTokenSource();
        _ = PlaySoloActionsAsync(actions, _soloActionCts.Token);
    }
}
