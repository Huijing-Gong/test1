using System;
using UnityEditor;
using UnityEngine;
public static class EnemyAttackPresentationChecks
{
    public static void Compile()
    {
        foreach (string name in new[] { "EnemyAttackEnergy", "EnemyAttackAfterimage", "HealingRecipientStatus", "RailBeetleWarningArrow" })
        {
            Shader shader=Resources.Load<Shader>(name);
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Attack VFX shader failed: "+name);
        }
        foreach(string path in new[] {
            "Assets/Art/Enemies/GelBlob/GelBlob.shader",
            "Assets/Art/Enemies/StoneScout/Variants/StoneScoutPaletteLit.shader" })
        {
            Shader shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
            if(!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException(path);
        }
        Debug.Log("ATTACK_VFX: PASS runtime scripts, four shader imports and resource inclusion.");
    }
}

