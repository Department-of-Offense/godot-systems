using Godot;

namespace Dof.Combat;

/// <summary>
/// A weapon type as data: author as a .tres in the editor and share it
/// between unit types. <see cref="CreateWeapon"/> makes an independent
/// <see cref="Weapon"/> node, so per-unit changes (upgrades, debuffs) never
/// touch the shared resource.
///
/// Subclass to add fields, and override <see cref="CreateWeapon"/> to return
/// your own Weapon subclass (projectiles, spread, beams).
/// </summary>
[GlobalClass]
public partial class WeaponDefinition : Resource
{
    [Export] public string DisplayName { get; set; } = "Weapon";
    [Export] public float Damage { get; set; } = 10f;
    [Export] public float FireInterval { get; set; } = 0.2f;
    [Export] public float Range { get; set; } = 40f;
    [Export] public bool FriendlyFire { get; set; }
    [Export] public bool ShowTracers { get; set; } = true;
    [Export] public Color TracerColor { get; set; } = new(1f, 0.85f, 0.4f);

    /// <summary>Shots per second, for UI and balancing.</summary>
    public float ShotsPerSecond => FireInterval <= 0f ? 0f : 1f / FireInterval;

    /// <summary>Damage per second if every shot hits, for UI and balancing.</summary>
    public float DamagePerSecond => Damage * ShotsPerSecond;

    public virtual Weapon CreateWeapon()
    {
        var weapon = new Weapon { Name = "Weapon" };
        ApplyTo(weapon);
        return weapon;
    }

    /// <summary>Copy this definition's values onto an existing weapon.</summary>
    public virtual void ApplyTo(Weapon weapon)
    {
        weapon.Damage = Damage;
        weapon.FireInterval = FireInterval;
        weapon.Range = Range;
        weapon.FriendlyFire = FriendlyFire;
        weapon.ShowTracers = ShowTracers;
        weapon.TracerColor = TracerColor;
    }
}
