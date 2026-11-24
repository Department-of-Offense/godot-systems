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

    // The groups below mirror Weapon; see its docs. Defaults are "off".
    [ExportGroup("Accuracy")]
    [Export(PropertyHint.Range, "0,30,0.1")] public float SpreadDegrees { get; set; }
    [Export(PropertyHint.Range, "0,10,0.05")] public float SpreadPerShot { get; set; }
    [Export(PropertyHint.Range, "0,45,0.5")] public float MaxSpreadDegrees { get; set; } = 10f;
    [Export] public float SpreadRecovery { get; set; } = 10f;
    [Export(PropertyHint.Range, "1,32,1")] public int Pellets { get; set; } = 1;

    [ExportGroup("Recoil")]
    [Export] public float RecoilPitchDegrees { get; set; }
    [Export] public float RecoilYawDegrees { get; set; }

    [ExportGroup("Ammo")]
    /// <summary>0 = bottomless.</summary>
    [Export] public int MagazineSize { get; set; }
    [Export] public float ReloadTime { get; set; } = 1.5f;
    [Export] public bool AutoReload { get; set; } = true;

    [ExportGroup("Projectile")]
    /// <summary>0 = hitscan.</summary>
    [Export] public float ProjectileSpeed { get; set; }
    [Export] public float ProjectileGravity { get; set; }
    [Export] public float SplashRadius { get; set; }
    [Export(PropertyHint.Range, "0,1,0.05")] public float SplashEdgeDamage { get; set; } = 0.25f;

    [ExportGroup("Aiming")]
    [Export(PropertyHint.Range, "0,1,0.05")] public float AdsSpreadMultiplier { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "1,8,0.05")] public float AdsZoom { get; set; } = 1.35f;

    /// <summary>Shots per second, for UI and balancing.</summary>
    public float ShotsPerSecond => FireInterval <= 0f ? 0f : 1f / FireInterval;

    /// <summary>
    /// Damage per second if every shot (and pellet) hits, for UI and
    /// balancing. Includes reload downtime when the weapon has a magazine.
    /// </summary>
    public float DamagePerSecond
    {
        get
        {
            if (FireInterval <= 0f) return 0f;
            var perPull = Damage * System.Math.Max(1, Pellets);
            if (MagazineSize <= 0) return perPull / FireInterval;
            return perPull * MagazineSize / (MagazineSize * FireInterval + ReloadTime);
        }
    }

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
        weapon.SpreadDegrees = SpreadDegrees;
        weapon.SpreadPerShot = SpreadPerShot;
        weapon.MaxSpreadDegrees = MaxSpreadDegrees;
        weapon.SpreadRecovery = SpreadRecovery;
        weapon.Pellets = Pellets;
        weapon.RecoilPitchDegrees = RecoilPitchDegrees;
        weapon.RecoilYawDegrees = RecoilYawDegrees;
        weapon.MagazineSize = MagazineSize;
        weapon.ReloadTime = ReloadTime;
        weapon.AutoReload = AutoReload;
        weapon.ProjectileSpeed = ProjectileSpeed;
        weapon.ProjectileGravity = ProjectileGravity;
        weapon.SplashRadius = SplashRadius;
        weapon.SplashEdgeDamage = SplashEdgeDamage;
        weapon.AdsSpreadMultiplier = AdsSpreadMultiplier;
        weapon.AdsZoom = AdsZoom;
        if (weapon.IsInsideTree()) weapon.Refill(); // a weapon not yet in the tree fills up on ready
    }
}
