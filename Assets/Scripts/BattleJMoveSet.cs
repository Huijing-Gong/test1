using UnityEngine;

// Player-only data: the opponent's BattleFourHitCombo remains unchanged.
public enum BattleJMove { Neutral, Side, Upper, Low }

public readonly struct BattleJMoveData
{
    public readonly float Damage, Knockback, Hitstun, Startup, Active, Recovery;
    public readonly float HorizontalWeight, VerticalWeight, ForwardSpeed;
    public readonly Vector3 Center, HalfExtents;
    public readonly bool Weapon;
    public BattleJMoveData(float damage, float knockback, float stun,
        float startup, float active, float recovery, float horizontal, float vertical,
        Vector3 center, Vector3 halfExtents, bool weapon = false, float forward = 0f)
    {
        Damage=damage; Knockback=knockback; Hitstun=stun;
        Startup=startup; Active=active; Recovery=recovery;
        HorizontalWeight=horizontal; VerticalWeight=vertical;
        Center=center; HalfExtents=halfExtents; Weapon=weapon; ForwardSpeed=forward;
    }
}

public static class BattleJMoveSet
{
    public const int HitCount = 4;
    public const float ComboWindow = .42f;
    public const float MissRecovery = .14f;
    public const float NeutralMissRecovery = .08f;
    public const float LinkHitSpeedCap = 1.25f;
    public static BattleJMoveData Get(BattleJMove move, int step = 0)
    {
        switch (move)
        {
            case BattleJMove.Side:
                return new BattleJMoveData(6f,.72f,.24f,.16f,.09f,.40f,.98f,.16f,
                    new Vector3(.94f,.08f,0),new Vector3(.72f,.36f,.6f),true);
            case BattleJMove.Upper:
                return new BattleJMoveData(5f,.72f,.29f,.12f,.09f,.32f,.22f,1f,
                    new Vector3(.20f,.70f,0),new Vector3(.85f,.83f,.6f));
            case BattleJMove.Low:
                return new BattleJMoveData(4f,.38f,.16f,.13f,.09f,.30f,1f,.06f,
                    new Vector3(.72f,-.58f,0),new Vector3(.52f,.23f,.6f),true);
            default:
                if (step <= 0) return new BattleJMoveData(3f,.20f,.27f,.105f,.075f,.12f,1f,.10f,
                    new Vector3(.69f,.12f,0),new Vector3(.51f,.32f,.6f),false,.8f);
                if (step == 1) return new BattleJMoveData(4f,.24f,.29f,.12f,.075f,.13f,1f,.10f,
                    new Vector3(.71f,.12f,0),new Vector3(.53f,.32f,.6f),true,.9f);
                if(step == 2) return new BattleJMoveData(5f,.26f,.36f,.15f,.09f,.13f,.95f,.20f,
                    new Vector3(.72f,.16f,0),new Vector3(.54f,.34f,.6f),false,.12f);
                return new BattleJMoveData(8f,.90f,.34f,.22f,.09f,.48f,.95f,.28f,
                    new Vector3(.85f,.18f,0),new Vector3(.64f,.38f,.6f),true,.18f);
        }
    }
}
