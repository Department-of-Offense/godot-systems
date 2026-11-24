using System;
using System.Collections.Generic;
using Godot;

namespace Dof.Debug;

/// <summary>
/// Immediate-mode debug drawing for any system: lines, paths, circles and
/// text in the world. Call it whenever you like (process or physics step).
/// A shape is drawn on the next rendered frame, then dropped, unless you pass
/// <c>seconds</c> to keep it around (handy for one-off events like a shot).
///
/// Shapes only show while a <see cref="DebugOverlay"/> is open. With no open
/// overlay every call returns at once, so leaving calls in shipping code costs
/// next to nothing. Lines draw on top of the scene, so they show through walls.
/// </summary>
public static class DebugDraw
{
    /// <summary>True while a debug overlay is open. Check it before building expensive debug text.</summary>
    public static bool Enabled { get; internal set; }

    /// <summary>Safety cap, so a runaway loop can't eat memory.</summary>
    public const int MaxItems = 50_000;

    internal struct LineItem
    {
        public Vector3 A, B;
        public Color Color;
        public double Until;
    }

    internal struct TextItem
    {
        public Vector3 At;
        public string Text;
        public Color Color;
        public double Until;
    }

    internal static readonly List<LineItem> Lines = new();
    internal static readonly List<TextItem> Texts = new();

    /// <summary>The overlay's clock, in seconds of game time.</summary>
    internal static double Now;

    /// <summary>Shapes waiting to be drawn (including ones kept for a while).</summary>
    public static int PendingLines => Lines.Count;
    public static int PendingTexts => Texts.Count;

    public static void Line(Vector3 a, Vector3 b, Color color, float seconds = 0f)
    {
        if (!Enabled || Lines.Count >= MaxItems) return;
        Lines.Add(new LineItem { A = a, B = b, Color = color, Until = Now + seconds });
    }

    /// <summary>Connected line through <paramref name="points"/>.</summary>
    public static void Path(ReadOnlySpan<Vector3> points, Color color, float seconds = 0f)
    {
        if (!Enabled) return;
        for (var i = 1; i < points.Length; i++) Line(points[i - 1], points[i], color, seconds);
    }

    /// <summary>Flat circle on the ground plane (XZ) around <paramref name="center"/>.</summary>
    public static void Circle(Vector3 center, float radius, Color color, int segments = 24, float seconds = 0f)
    {
        if (!Enabled || radius <= 0f) return;
        segments = Math.Max(3, segments);
        var previous = center + new Vector3(radius, 0, 0);
        for (var i = 1; i <= segments; i++)
        {
            var angle = Mathf.Tau * i / segments;
            var next = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            Line(previous, next, color, seconds);
            previous = next;
        }
    }

    /// <summary>Three-axis cross marking a point.</summary>
    public static void Cross(Vector3 at, float size, Color color, float seconds = 0f)
    {
        if (!Enabled) return;
        var h = size / 2f;
        Line(at - new Vector3(h, 0, 0), at + new Vector3(h, 0, 0), color, seconds);
        Line(at - new Vector3(0, h, 0), at + new Vector3(0, h, 0), color, seconds);
        Line(at - new Vector3(0, 0, h), at + new Vector3(0, 0, h), color, seconds);
    }

    /// <summary>Line with a small head at <paramref name="to"/>.</summary>
    public static void Arrow(Vector3 from, Vector3 to, Color color, float seconds = 0f)
    {
        if (!Enabled) return;
        Line(from, to, color, seconds);
        var direction = to - from;
        var length = direction.Length();
        if (length < 0.001f) return;
        direction /= length;
        var side = direction.Cross(Mathf.Abs(direction.Y) > 0.99f ? Vector3.Right : Vector3.Up).Normalized();
        var head = Mathf.Min(0.5f, length * 0.3f);
        Line(to, to - direction * head + side * head * 0.5f, color, seconds);
        Line(to, to - direction * head - side * head * 0.5f, color, seconds);
    }

    /// <summary>Text at a world point, drawn on screen (with a shadow). May contain line breaks.</summary>
    public static void Text(Vector3 at, string text, Color? color = null, float seconds = 0f)
    {
        if (!Enabled || Texts.Count >= MaxItems) return;
        Texts.Add(new TextItem { At = at, Text = text, Color = color ?? Colors.White, Until = Now + seconds });
    }

    /// <summary>Drop everything, including shapes kept with <c>seconds</c>.</summary>
    public static void Clear()
    {
        Lines.Clear();
        Texts.Clear();
    }

    /// <summary>Drop what has been drawn and has run out of time.</summary>
    internal static void Prune()
    {
        Lines.RemoveAll(l => l.Until <= Now);
        Texts.RemoveAll(t => t.Until <= Now);
    }
}
