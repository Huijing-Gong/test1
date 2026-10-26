using UnityEngine;

public enum AttackImpactKind { Normal, Heavy, Charged }

public readonly struct DamageInfo
{
    public int Amount { get; }
    public GameObject Source { get; }
    public Vector3 HitPoint { get; }
    public Vector3 HitDirection { get; }
    public AttackImpactKind ImpactKind { get; }
    public bool IsValid => Amount > 0;

    public DamageInfo(int amount, GameObject source, Vector3 hitPoint, Vector3 hitDirection,
        AttackImpactKind impactKind = AttackImpactKind.Normal)
    {
        Amount = amount;
        Source = source;
        ImpactKind = impactKind;
        HitPoint = hitPoint;
        HitDirection = hitDirection.sqrMagnitude > 0.000001f
            ? hitDirection.normalized
            : Vector3.zero;
    }
}
