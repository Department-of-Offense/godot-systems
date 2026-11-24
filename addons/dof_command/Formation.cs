using System.Collections.Generic;
using Dof.Core;
using Dof.Units;
using Godot;

namespace Dof.Command;

/// <summary>Spreads a group order across several units so they don't all pile onto one point.</summary>
public static class Formation
{
    /// <summary>
    /// Grid slots centred on <paramref name="center"/>, facing along
    /// <paramref name="facing"/> (row 0 is the front row).
    /// </summary>
    public static List<Vector3> Grid(Vector3 center, Vector3 facing, int count, float spacing)
    {
        var slots = new List<Vector3>(count);
        if (count <= 0) return slots;

        var cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        var rows = Mathf.CeilToInt(count / (float)cols);
        var flat = Math3D.Flat(facing);
        var basis = new Basis(Vector3.Up, flat.LengthSquared() > 0.0001f ? Math3D.YawFor(flat) : 0f);

        for (var i = 0; i < count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            // The last row may be short; centre it.
            var colsInRow = row == rows - 1 ? count - row * cols : cols;
            var x = (col - (colsInRow - 1) * 0.5f) * spacing;
            var z = (row - (rows - 1) * 0.5f) * spacing;
            slots.Add(center + basis * new Vector3(x, 0f, z));
        }
        return slots;
    }

    /// <summary>Greedy nearest-slot assignment so units don't cross paths more than they need to.</summary>
    public static Dictionary<Unit, Vector3> Assign(IReadOnlyList<Unit> units, Vector3 target, float spacing)
    {
        var result = new Dictionary<Unit, Vector3>();
        if (units.Count == 0) return result;

        var centroid = Vector3.Zero;
        foreach (var unit in units) centroid += unit.GlobalPosition;
        centroid /= units.Count;

        var slots = Grid(target, target - centroid, units.Count, spacing);
        var remaining = new List<Unit>(units);
        foreach (var slot in slots)
        {
            var best = 0;
            for (var i = 1; i < remaining.Count; i++)
                if (remaining[i].GlobalPosition.DistanceSquaredTo(slot) < remaining[best].GlobalPosition.DistanceSquaredTo(slot))
                    best = i;
            result[remaining[best]] = slot;
            remaining.RemoveAt(best);
        }
        return result;
    }
}
