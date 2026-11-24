using System;
using System.Collections.Generic;
using System.Text;
using Dof.Core;
using Godot;

namespace Dof.Debug;

/// <summary>
/// A block of the debug overlay: some live text in the panel, and optionally
/// world drawing through <see cref="DebugDraw"/>. Add your own with
/// <see cref="DebugOverlay.AddSection"/> for anything your game wants to watch.
/// </summary>
public interface IDebugSection
{
    string Title { get; }

    /// <summary>Called once when added. Register toggles here with <see cref="DebugOverlay.AddToggle"/>.</summary>
    void Attach(DebugOverlay overlay) { }

    /// <summary>Panel text. Called a few times a second while the overlay is open.</summary>
    void Write(StringBuilder text);

    /// <summary>Called every rendered frame while open: draw into the world here.</summary>
    void Draw(double delta) { }
}

/// <summary>An on/off switch shown as a checkbox in the overlay panel.</summary>
public sealed class DebugToggle
{
    public string Name { get; }
    internal CheckBox? Box;
    private bool _on;

    internal DebugToggle(string name, bool on)
    {
        Name = name;
        _on = on;
    }

    public bool On
    {
        get => _on;
        set
        {
            _on = value;
            Box?.SetPressedNoSignal(value);
        }
    }
}

public static class DebugActions
{
    public const string Toggle = "dof_debug_toggle";

    public static void EnsureDefaults() => InputDefaults.Keys(Toggle, Key.F3);
}

/// <summary>
/// Developer overlay, opened and closed with F3 (<see cref="DebugActions.Toggle"/>).
/// Shows a panel of live sections (performance by default; add your own
/// with <see cref="AddSection"/>) and renders everything drawn through
/// <see cref="DebugDraw"/>. Add one anywhere in the scene. It keeps working
/// while the game is paused.
/// </summary>
[GlobalClass]
public partial class DebugOverlay : CanvasLayer
{
    [Export] public string ToggleAction { get; set; } = DebugActions.Toggle;
    [Export] public bool StartOpen { get; set; }

    /// <summary>Include the built-in frame time, node count and memory section.</summary>
    [Export] public bool ShowPerformance { get; set; } = true;

    [Export(PropertyHint.Range, "0.05,2,0.05")] public double PanelRefreshSeconds { get; set; } = 0.25;
    [Export] public int TextFontSize { get; set; } = 13;

    /// <summary>The overlay that <see cref="DebugDraw"/> is drawing into, if one is open.</summary>
    public static DebugOverlay? Current { get; private set; }

    public bool IsOpen { get; private set; }
    public IReadOnlyList<IDebugSection> Sections => _sections;
    public IReadOnlyList<DebugToggle> Toggles => _toggles;

    /// <summary>The panel's current text (for tests and logging).</summary>
    public string PanelText => _text?.Text ?? "";

    /// <summary>Lines drawn last frame, and texts that landed on screen.</summary>
    public int RenderedLines { get; private set; }
    public int RenderedTexts { get; private set; }

    private readonly List<IDebugSection> _sections = new();
    private readonly List<DebugToggle> _toggles = new();
    private readonly List<Label> _labels = new();
    private readonly HashSet<IDebugSection> _failed = new();
    private Control? _panel;
    private Control? _world;
    private Label? _text;
    private HFlowContainer? _toggleBox;
    private MeshInstance3D? _lines;
    private ArrayMesh? _mesh;
    private Vector3[] _vertices = Array.Empty<Vector3>();
    private Color[] _colors = Array.Empty<Color>();
    private readonly Godot.Collections.Array _arrays = new();
    private StandardMaterial3D? _material;
    private double _refreshIn;

    public DebugOverlay()
    {
        Layer = 100;
        ProcessPriority = int.MaxValue; // draw after everything else has queued its shapes
        ProcessMode = ProcessModeEnum.Always;
    }

    public void AddSection(IDebugSection section)
    {
        _sections.Add(section);
        section.Attach(this);
        _refreshIn = 0;
    }

    /// <summary>A checkbox in the panel. Asking for a name that exists returns the same toggle.</summary>
    public DebugToggle AddToggle(string name, bool on = true)
    {
        var existing = _toggles.Find(t => t.Name == name);
        if (existing is not null) return existing;
        var toggle = new DebugToggle(name, on);
        _toggles.Add(toggle);
        if (_toggleBox is not null) AddCheckBox(toggle);
        return toggle;
    }

    public override void _Ready()
    {
        DebugActions.EnsureDefaults();
        if (ShowPerformance)
        {
            IDebugSection performance = new PerformanceSection();
            _sections.Insert(0, performance);
            performance.Attach(this);
        }
        BuildUi();
        SetOpen(StartOpen);
    }

    public override void _ExitTree()
    {
        if (Current == this) Close();
    }

    public void SetOpen(bool open)
    {
        IsOpen = open;
        if (_panel is not null) _panel.Visible = open;
        if (_world is not null) _world.Visible = open;
        if (_lines is not null) _lines.Visible = open;
        if (open)
        {
            Current = this;
            DebugDraw.Enabled = true;
            _refreshIn = 0;
        }
        else if (Current == this)
        {
            Close();
        }
    }

    private void Close()
    {
        Current = null;
        DebugDraw.Enabled = false;
        DebugDraw.Clear();
        foreach (var label in _labels) label.Visible = false;
        _mesh?.ClearSurfaces();
        RenderedLines = RenderedTexts = 0;
    }

    public override void _Input(InputEvent @event)
    {
        if (!InputMap.HasAction(ToggleAction) || !@event.IsActionPressed(ToggleAction)) return;
        SetOpen(!IsOpen);
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        DebugDraw.Now += delta;

        foreach (var section in _sections)
        {
            if (_failed.Contains(section)) continue;
            try { section.Draw(delta); }
            catch (Exception e) { Fail(section, e); }
        }

        RenderLines();
        RenderTexts();

        _refreshIn -= delta;
        if (_refreshIn <= 0)
        {
            _refreshIn = PanelRefreshSeconds;
            RefreshPanel();
        }

        DebugDraw.Prune();
    }

    /// <summary>Rebuild the panel text now instead of at the next refresh.</summary>
    public void RefreshPanel()
    {
        var text = new StringBuilder();
        foreach (var section in _sections)
        {
            if (_failed.Contains(section)) continue;
            text.Append("— ").Append(section.Title).AppendLine(" —");
            try { section.Write(text); }
            catch (Exception e) { Fail(section, e); }
        }
        // AppendLine writes \r\n on Windows, which a Label shows as an extra blank line.
        if (_text is not null) _text.Text = text.ToString().Replace("\r\n", "\n").TrimEnd();
    }

    /// <summary>A section that throws is switched off, so one bug can't take the whole overlay down.</summary>
    private void Fail(IDebugSection section, Exception e)
    {
        _failed.Add(section);
        SectionFailed(section, e);
    }

    /// <summary>Called once when a section throws and is switched off. Logs an engine error by default.</summary>
    public Action<IDebugSection, Exception> SectionFailed { get; set; } = (section, e) =>
        GD.PushError($"Debug section '{section.Title}' failed and was switched off: {e}");

    private void RenderLines()
    {
        var mesh = _mesh!;
        mesh.ClearSurfaces();
        var lines = DebugDraw.Lines;
        RenderedLines = lines.Count;
        if (lines.Count == 0) return;

        // One engine call with packed arrays: per-vertex calls cost milliseconds
        // with a few thousand lines. Arrays grow in powers of two and the unused
        // tail is zero-length lines, which draw nothing, so frames don't allocate.
        var needed = lines.Count * 2;
        if (_vertices.Length < needed)
        {
            var size = Math.Max(256, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)needed));
            _vertices = new Vector3[size];
            _colors = new Color[size];
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            _vertices[2 * i] = line.A;
            _vertices[2 * i + 1] = line.B;
            _colors[2 * i] = _colors[2 * i + 1] = line.Color;
        }
        Array.Clear(_vertices, needed, _vertices.Length - needed);
        _arrays[(int)Mesh.ArrayType.Vertex] = _vertices;
        _arrays[(int)Mesh.ArrayType.Color] = _colors;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, _arrays);
        mesh.SurfaceSetMaterial(0, _material);
    }

    /// <summary>Width of a text block on screen, in pixels. Text is centred on its point.</summary>
    private const float TextWidth = 240f;

    /// <summary>
    /// Texts are pooled Labels: a Label lays its text out once and redraws it
    /// cheaply until the text changes, where drawing strings by hand redoes the
    /// layout every frame (milliseconds for a few dozen unit labels).
    /// </summary>
    private void RenderTexts()
    {
        var camera = GetViewport().GetCamera3D();
        var shown = 0;
        if (camera is not null && _world is not null)
        {
            var bounds = _world.GetViewportRect().Grow(50);
            foreach (var item in DebugDraw.Texts)
            {
                if (!TryProject(camera, item.At, bounds, out var at)) continue;
                var label = shown < _labels.Count ? _labels[shown] : NewLabel();
                if (label.Text != item.Text) label.Text = item.Text;
                if (label.SelfModulate != item.Color) label.SelfModulate = item.Color;
                label.Position = at - new Vector2(TextWidth / 2f, 0);
                label.Visible = true;
                shown++;
            }
        }
        for (var i = shown; i < _labels.Count; i++)
            if (_labels[i].Visible) _labels[i].Visible = false;
        RenderedTexts = shown;
    }

    private Label NewLabel()
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(TextWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", TextFontSize);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 4);
        _world!.AddChild(label);
        _labels.Add(label);
        return label;
    }

    /// <summary>Screen point for a world point, or false if it is behind the camera or off screen.</summary>
    internal static bool TryProject(Camera3D camera, Vector3 world, Rect2 bounds, out Vector2 screen)
    {
        screen = default;
        if (camera.IsPositionBehind(world)) return false;
        screen = camera.UnprojectPosition(world);
        return bounds.HasPoint(screen);
    }

    private void BuildUi()
    {
        _world = new Control { Name = "WorldText", MouseFilter = Control.MouseFilterEnum.Ignore };
        _world.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_world);

        var panel = new PanelContainer
        {
            Name = "Panel",
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -400, OffsetRight = -12, OffsetTop = 12,
            GrowHorizontal = Control.GrowDirection.Begin,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.78f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        var column = new VBoxContainer();
        var title = new Label { Text = $"DEBUG   {KeyHint()} to close" };
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        column.AddChild(title);
        _text = new Label();
        _text.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(_text);
        _toggleBox = new HFlowContainer();
        column.AddChild(_toggleBox);
        panel.AddChild(column);
        AddChild(panel);
        _panel = panel;
        foreach (var toggle in _toggles) AddCheckBox(toggle);

        _mesh = new ArrayMesh();
        _arrays.Resize((int)Mesh.ArrayType.Max);
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            RenderPriority = (int)Material.RenderPriorityMax,
        };
        _lines = new MeshInstance3D
        {
            Name = "Lines",
            Mesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            // Lines sit anywhere in the world; never cull them by the mesh's stale bounds.
            ExtraCullMargin = 16384f,
        };
        AddChild(_lines);
    }

    private void AddCheckBox(DebugToggle toggle)
    {
        var box = new CheckBox { Text = toggle.Name, ButtonPressed = toggle.On, FocusMode = Control.FocusModeEnum.None };
        box.AddThemeFontSizeOverride("font_size", 12);
        box.Toggled += pressed => toggle.On = pressed;
        toggle.Box = box;
        _toggleBox!.AddChild(box);
    }

    private string KeyHint()
    {
        if (!InputMap.HasAction(ToggleAction)) return ToggleAction;
        foreach (var e in InputMap.ActionGetEvents(ToggleAction))
            if (e is InputEventKey key) return OS.GetKeycodeString(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode);
        return ToggleAction;
    }
}

/// <summary>Frame and physics time, worst frame of the last second, node and object counts, draw calls, C# memory.</summary>
public sealed class PerformanceSection : IDebugSection
{
    public string Title => "Performance";

    private double _worst, _window, _shownWorst;

    public void Draw(double delta)
    {
        _worst = Math.Max(_worst, delta);
        _window += delta;
        if (_window < 1.0) return;
        _shownWorst = _worst;
        _worst = 0;
        _window = 0;
    }

    public void Write(StringBuilder text)
    {
        static double M(Performance.Monitor monitor) => Performance.GetMonitor(monitor);
        text.AppendLine($"{M(Performance.Monitor.TimeFps):0} fps   process {M(Performance.Monitor.TimeProcess) * 1000:0.0} ms   physics {M(Performance.Monitor.TimePhysicsProcess) * 1000:0.0} ms");
        text.AppendLine($"worst frame (last 1 s) {_shownWorst * 1000:0.0} ms");
        text.AppendLine($"nodes {M(Performance.Monitor.ObjectNodeCount):0}   objects {M(Performance.Monitor.ObjectCount):0}   draw calls {M(Performance.Monitor.RenderTotalDrawCallsInFrame):0}");
        text.AppendLine($"active bodies {M(Performance.Monitor.Physics3DActiveObjects):0}   C# heap {GC.GetTotalMemory(false) / 1048576.0:0.0} MB   GCs {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
    }
}
