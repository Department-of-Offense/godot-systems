using Godot;

namespace CommandEngine.Units;

/// <summary>Hit points for anything that can be destroyed. Lives as a child node of a <see cref="Unit"/>.</summary>
[GlobalClass]
public partial class Health : Node
{
    [Signal] public delegate void DamagedEventHandler(float amount, Node? source);
    [Signal] public delegate void DiedEventHandler(Node? source);

    [Export] public float MaxHealth { get; set; } = 100f;

    public float Current { get; private set; }
    public bool IsAlive => Current > 0f;
    public float Fraction => MaxHealth <= 0f ? 0f : Current / MaxHealth;

    public override void _Ready() => Current = MaxHealth;

    public void Reset(float? maxHealth = null)
    {
        if (maxHealth.HasValue) MaxHealth = maxHealth.Value;
        Current = MaxHealth;
    }

    public void TakeDamage(float amount, Node? source = null)
    {
        if (!IsAlive || amount <= 0f) return;
        Current = Mathf.Max(0f, Current - amount);
        EmitSignal(SignalName.Damaged, amount, source!);
        if (!IsAlive) EmitSignal(SignalName.Died, source!);
    }
}
