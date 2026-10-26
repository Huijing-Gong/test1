using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SamProduction.BossArena;
public static class BossPunchSetup
{
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new System.Exception("Stop play first");
        var motor=Object.FindFirstObjectByType<ArenaEclipseBossMotor>();
        var library=motor.GetComponent<ArenaPunchLibrary>()??motor.gameObject.AddComponent<ArenaPunchLibrary>();
        var imported=AssetDatabase.LoadAllAssetsAtPath("Assets/BossPunchCandidates/Fighting.fbx").OfType<AnimationClip>();
        string[] names={"Fists_Punch_L","Fists_Punch_R","Fists_Punch_Move_L","Fists_Punch_Move_R","Fists_Punch_Heavy2Idle","Punch_Jab","Punch_Cross","Melee_Hook"};
        float[] times={.167f,.187f,.278f,.299f,.511f,.202f,.283f,.272f};
        bool[] left={true,false,true,false,false,true,false,false};
        library.punches=new ArenaPunchLibrary.Punch[8];
        for(int i=0;i<8;i++)
        {
            var clip=imported.FirstOrDefault(c=>c.name==names[i])??AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Art/ThirdParty/Quaternius/UniversalFighters/Animations/"+names[i]+".anim");
            var bindings=AnimationUtility.GetCurveBindings(clip);
            library.punches[i]=new ArenaPunchLibrary.Punch{attack=ArenaAttack.BoxLeft+i,clip=clip,
                rootX=AnimationUtility.GetEditorCurve(clip,bindings.First(b=>b.propertyName=="RootT.x")),rootZ=AnimationUtility.GetEditorCurve(clip,bindings.First(b=>b.propertyName=="RootT.z")),
                minimumRange=i==4?1.8f:i==7?1.9f:i==2||i==3?1.4f:.75f,
                maximumRange=i==4?2.2f:i==7?2.8f:i==2||i==3?2f:1.5f,
                contacts=new[]{new ArenaPunchLibrary.Contact{left=left[i],time=times[i],damage=i==4?26:i==7?22:18}}};
        }
        library.punches[4].contacts=new[]{new ArenaPunchLibrary.Contact{left=true,time=.333f,damage=14},new ArenaPunchLibrary.Contact{left=false,time=.511f,damage=26}};
        motor.punches=library;EditorUtility.SetDirty(motor);EditorUtility.SetDirty(library);
        EditorSceneManager.MarkSceneDirty(motor.gameObject.scene);EditorSceneManager.SaveScene(motor.gameObject.scene);
    }
}
