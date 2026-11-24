using Godot;

namespace CommandEngine.Units;

public static class UnitMath
{
    /// <summary>Yaw (radians) that makes a node's -Z axis point along <paramref name="direction"/>.</summary>
    public static float YawFor(Vector3 direction) => Mathf.Atan2(-direction.X, -direction.Z);

    /// <summary>Unit vector for a yaw/pitch pair, matching <see cref="Basis.FromEuler"/> YXZ order.</summary>
    public static Vector3 DirectionFor(float yaw, float pitch) =>
        Basis.FromEuler(new Vector3(pitch, yaw, 0f)) * Vector3.Forward;

    /// <summary>Frame-rate independent smoothing factor for exponential lerps.</summary>
    public static float Damp(float sharpness, double delta) => 1f - Mathf.Exp(-sharpness * (float)delta);

    public static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}
