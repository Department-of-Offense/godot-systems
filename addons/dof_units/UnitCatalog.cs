using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Dof.Units;

/// <summary>
/// The unit types a game knows about, keyed by <see cref="UnitDefinition.Id"/>.
/// <see cref="LoadFolder"/> picks up every UnitDefinition .tres in a folder,
/// so adding a unit type means dropping a file in, no code change.
/// </summary>
public sealed class UnitCatalog
{
    private readonly Dictionary<string, UnitDefinition> _byId = new();
    private readonly List<UnitDefinition> _ordered = new();

    public IReadOnlyList<UnitDefinition> All => _ordered;
    public int Count => _ordered.Count;

    /// <summary>Load every UnitDefinition resource under <paramref name="folder"/> (e.g. "res://units").</summary>
    public static UnitCatalog LoadFolder(string folder, bool recursive = true)
    {
        var catalog = new UnitCatalog();
        catalog.AddFolder(folder, recursive);
        return catalog;
    }

    public void AddFolder(string folder, bool recursive = true)
    {
        folder = folder.TrimEnd('/');
        foreach (var file in DirAccess.GetFilesAt(folder).OrderBy(f => f))
        {
            // Exported builds list "x.tres.remap"; loading "x.tres" still works.
            var name = file.EndsWith(".remap") ? file[..^".remap".Length] : file;
            if (!name.EndsWith(".tres") && !name.EndsWith(".res")) continue;
            if (ResourceLoader.Load($"{folder}/{name}") is UnitDefinition definition) Add(definition);
        }
        if (!recursive) return;
        foreach (var sub in DirAccess.GetDirectoriesAt(folder).OrderBy(d => d))
            AddFolder($"{folder}/{sub}", true);
    }

    /// <summary>Add a definition. Ids must be unique and non-empty.</summary>
    public bool Add(UnitDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.Id))
        {
            GD.PushWarning($"UnitCatalog: skipping '{definition.ResourcePath}', it has no Id.");
            return false;
        }
        if (!_byId.TryAdd(definition.Id, definition))
        {
            GD.PushWarning($"UnitCatalog: duplicate Id '{definition.Id}' in '{definition.ResourcePath}' (already from '{_byId[definition.Id].ResourcePath}'); skipped.");
            return false;
        }
        _ordered.Add(definition);
        return true;
    }

    public UnitDefinition Get(string id) =>
        _byId.TryGetValue(id, out var definition) ? definition : throw new KeyNotFoundException($"No unit type with Id '{id}'.");

    public bool TryGet(string id, out UnitDefinition definition) => _byId.TryGetValue(id, out definition!);

    public IEnumerable<UnitDefinition> WithTag(string tag) => _ordered.Where(d => d.HasTag(tag));
}
