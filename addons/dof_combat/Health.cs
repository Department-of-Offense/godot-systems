using Godot;

namespace Dof.Combat;

/// <summary>Hit points for anything that can be destroyed. Add as a child node.</summary>
[GlobalClass]
public partial class Health : Node
{
    [Signal] public delegate void DamagedEventHandler(float amount, Node? source);
    [Signal] public delegate void HealedEventHandler(float amount);
    [Signal] public delegate void DiedEventHandler(Node? source);

    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public bool Invulnerable { get; set; }

    public float Current { get; private set; }
    public bool IsAlive => Current > 0f;
    public float Fraction => MaxHealth <= 0f ? 0f : Current / MaxHealth;

    public override void _Ready() => Current = MaxHealth;

    public void Reset(float? maxHealth = null)
    {
        if (maxHealth.HasValue) MaxHealth = maxHealth.Value;
        Current = MaxHealth;
    }

    /// <summary>Override to add armour, resistances, shields and so on.</summary>
    protected virtual float ModifyDamage(float amount, Node? source) => amount;

    public void TakeDamage(float amount, Node? source = null)
    {
        if (!IsAlive || Invulnerable || amount <= 0f) return;
        amount = ModifyDamage(amount, source);
        if (amount <= 0f) return;
        Current = Mathf.Max(0f, Current - amount);
        EmitSignal(SignalName.Damaged, amount, source!);
        if (!IsAlive) EmitSignal(SignalName.Died, source!);
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f) return;
        var before = Current;
        Current = Mathf.Min(MaxHealth, Current + amount);
        if (Current > before) EmitSignal(SignalName.Healed, Current - before);
    }
}
