using System.Text.Json.Serialization;

namespace SpawnDev.AI.Demo.Reachy;

/// <summary>Options for the <c>ReachyMini</c> constructor.</summary>
public class ReachyMiniOptions
{
    /// <summary>Label advertised to the robot.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppName { get; set; }

    /// <summary>OAuth client / app id.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; set; }

    /// <summary>Signalling server. Defaults to the central Hugging Face Space when omitted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignalingUrl { get; set; }
}

/// <summary>
/// A target pose for <c>gotoTarget</c>, in the daemon's raw wire units.
/// </summary>
/// <remarks>
/// ⚠️ <see cref="BodyYaw"/> is <c>body_yaw</c> on the wire - snake_case, unlike every other member here -
/// so it carries an explicit name. Left to the default policy it would serialize as <c>bodyYaw</c>, the
/// daemon would not recognise it, and the body simply would not turn. Nothing throws: an unknown key is
/// ignored and a missing one means "unchanged".
/// </remarks>
public class ReachyGotoTarget
{
    /// <summary>Head pose as a flat 4x4, world frame.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? Head { get; set; }

    /// <summary>Antennas as <c>[rightRad, leftRad]</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? Antennas { get; set; }

    /// <summary>Body yaw in radians.</summary>
    [JsonPropertyName("body_yaw")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BodyYaw { get; set; }

    /// <summary>Interpolation time in seconds.</summary>
    public double Duration { get; set; }
}
