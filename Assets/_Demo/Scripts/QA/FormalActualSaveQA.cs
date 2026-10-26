using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit isolated process persistence checks. No natural/hardware/UI claim.
public sealed class FormalActualSaveQA : MonoBehaviour
{
    [Serializable] class Fixture { public string prefix,primary,backup,sourceReceipt; public string[] extraPrefixes; }
    [Serializable] class CameraSnapshot { public float delay;public Vector3 camera,player;public bool playerInFrustum; }
    [Serializable] class Receipt
    {
        public string scope="EXACT_REAL_SAVE_CLONE_CROSSPROCESS_V2_MIGRATION_OR_CURRENT_CONTINUE_API_NOT_UI_OR_FULL_SAVE_ACCEPTANCE";
        public string mode,version,buildGuid,executable,prefix,error,primarySha256,backupSha256,restoredJson;
        public bool passed,qaStoreCleared,migrationExpected,migrationPersisted;public int sourceMapVersion,targetMapVersion; public string[] assertions; public CameraSnapshot[] cameraSnapshots;
        public Vector3 actualPlayerPosition;public float actualPlayerYaw,damageBonus,sprintDrainMultiplier;
    }
    const string Main="Assets/_Demo/Scenes/OverworldBaseline.unity";
    readonly List<string> assertions=new List<string>();Fixture fixture;Receipt receipt;string output,mode;
    BaselineFlow flow;BaselineSaveData expected;bool complete;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--actual-save-check")<0)return;
        Application.runInBackground=true;
        var go=new GameObject("Opt-in exact formal-save crossprocess QA");DontDestroyOnLoad(go);go.AddComponent<FormalActualSaveQA>();
    }
    IEnumerator Start()
    {
        yield return null;yield return null;
        try
        {
            var args=Environment.GetCommandLineArgs();mode=Arg(args,"--actual-save-mode");
            output=Path.GetFullPath(Arg(args,"--sam-output"));if(Directory.Exists(output))throw new IOException("Preserve earlier receipt");Directory.CreateDirectory(output);
            fixture=JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.GetFullPath(Arg(args,"--actual-save-fixture"))));
            receipt=new Receipt{mode=mode,prefix=fixture.prefix,version=Application.version,buildGuid=Application.buildGUID,executable=Environment.GetCommandLineArgs()[0]};
            Require(Application.companyName=="CodexUnityTakeoverQA20261004"&&Application.productName=="Current Character Arena QA","isolated QA company/product only");
            Require(fixture.prefix.StartsWith("QA.Takeover.ActualSave.")&&fixture.prefix!=BaselineSaveStore.DefaultKeyPrefix,"unique QA prefix, never original slot");
            Require(mode=="seed"||mode=="continue"||mode=="migrate"||mode=="cleanup","explicit seed, independent continue, or owned QA cleanup process");
            if(mode=="cleanup")
            {
                foreach(string prefix in fixture.extraPrefixes??new string[0])
                {
                    Require(System.Text.RegularExpressions.Regex.IsMatch(prefix,@"^QA\.Takeover\.(ActualSave|IntegrationA04)\.[0-9a-f]{32}\.Save$"),"bounded owned QA prefix "+prefix);
                    BaselineSaveStore.Clear(prefix);Require(!BaselineSaveStore.HasAnyData(prefix),"owned QA prefix cleared "+prefix);
                }
                Finish(null,true);yield break;
            }
            if(mode=="seed")
            {
                Require(!BaselineSaveStore.HasAnyData(fixture.prefix),"seed process starts with absent QA slot");
                PlayerPrefs.SetString(fixture.prefix+".primary",fixture.primary);PlayerPrefs.SetString(fixture.prefix+".backup",fixture.backup);PlayerPrefs.Save();
            }
            else Require(BaselineSaveStore.HasAnyData(fixture.prefix),"reader sees previous process durable QA slot before any writes");
            Require(PlayerPrefs.GetString(fixture.prefix+".primary")==fixture.primary&&PlayerPrefs.GetString(fixture.prefix+".backup")==fixture.backup,"exact source envelopes retained, no payload editing");
            receipt.primarySha256=Sha(PlayerPrefs.GetString(fixture.prefix+".primary"));receipt.backupSha256=Sha(PlayerPrefs.GetString(fixture.prefix+".backup"));
            Require(BaselineSaveStore.TryLoad(fixture.prefix,out expected,out var message),"original store validates cloned envelope: "+message);receipt.sourceMapVersion=expected.mapVersion;
            if(mode=="seed"){Finish(null,false);yield break;}
            SceneManager.sceneLoaded+=PrepareMain;
        }
        catch(Exception error){Fail(error);yield break;}
        yield return SceneManager.LoadSceneAsync(Main,LoadSceneMode.Single);yield return null;
        try
        {
            Require(flow&&flow.saveKeyPrefix==fixture.prefix,"original saved MainQA flow bound before Start to unique QA store");
            PrepareExpectedMapContract();Require(flow.ContinueGame(),"original API Continue accepts cloned real progress in new process");
        }
        catch(Exception error){Fail(error);yield break;}
        yield return null;yield return null;
        try
        {
            Require(flow.Started&&!flow.BlockWorldInput,"restored main world unlocked");
            Require(JsonUtility.ToJson(flow.Progress)==JsonUtility.ToJson(expected),"all serialized current-map progress fields retained exactly");
            Require(GameSession.Instance.HasSword==expected.hasSword&&GameSession.Instance.BossDefeated==expected.bossDefeated,"session sword and ending mirrors retained");
            Require(SideWorldTravel.Instance&&SideWorldTravel.Instance.TrialACompleted==expected.trialACompleted&&SideWorldTravel.Instance.TrialBCompleted==expected.trialBCompleted,"A/B completion mirrors restored without side-game changes");
            var world=flow.GetComponent<BaselineWorld>();var loadout=world?world.player.GetComponent<BaselineLoadout>():null;
            Require(world&&loadout,"existing world and equipment owner found");
            for(int i=0;i<5;i++)
            {
                bool owns=i<3?expected.opened[i]:expected.trialRewardClaimed[i-3];
                Require(loadout.OwnsEquipment(i)==owns&&loadout.IsEquipped(i)==(owns&&(expected.unequippedMask&(1<<i))==0),"existing equipment ownership/equipped bit "+i+" retained");
            }
            int bonus=Enumerable.Range(0,5).Where(loadout.IsEquipped).Sum(loadout.EquipmentBonus);
            Require(loadout.DamageBonus==bonus,"restored equipment damage effects retained");
            Require(Mathf.Approximately(loadout.SprintDrainMultiplier,loadout.IsEquipped(1)?.7f:1),"existing Ridge sprint effect retained");
            Vector3 position=world.player.position;
            Require(new Vector2(position.x-expected.safePosition.x,position.z-expected.safePosition.z).magnitude<.01f,"saved horizontal standing point restored");
            Require(Mathf.Abs(Mathf.DeltaAngle(world.player.eulerAngles.y,expected.safeYaw))<.01f,"saved character yaw restored");
            Require(world.NavigationGround(expected.safePosition,out var ground)&&Mathf.Abs(position.y-(ground.y-.10f))<.08f,"standing height follows existing ground restoration contract");
            if(!receipt.migrationExpected)Require(PlayerPrefs.GetString(fixture.prefix+".primary")==fixture.primary&&PlayerPrefs.GetString(fixture.prefix+".backup")==fixture.backup,"Continue did not rewrite valid current-map source envelopes");
            else{Require(BaselineSaveStore.TryLoad(fixture.prefix,out var stored,out var saveMessage)&&JsonUtility.ToJson(stored)==JsonUtility.ToJson(expected),"approved map migration persisted all expected progress fields");Require(PlayerPrefs.GetString(fixture.prefix+".backup")==fixture.primary,"migration backup retains exact pre-migration primary");receipt.migrationPersisted=true;}
            if(!expected.hasSword)Require(!flow.EnterEnding(),"original no-sword progress is preserved and ending entrance does not bypass sword requirement");
            receipt.actualPlayerPosition=position;receipt.actualPlayerYaw=world.player.eulerAngles.y;
            receipt.damageBonus=loadout.DamageBonus;receipt.sprintDrainMultiplier=loadout.SprintDrainMultiplier;receipt.restoredJson=JsonUtility.ToJson(flow.Progress);
        }
        catch(Exception error){Fail(error);yield break;}
        var cameras=new List<CameraSnapshot>();
        for(int i=0;i<3;i++)
        {
            if(i>0)yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            var world=flow.GetComponent<BaselineWorld>();var camera=Camera.main;
            var planes=GeometryUtility.CalculateFrustumPlanes(camera);
            bool visible=world.player.GetComponentsInChildren<Renderer>().Any(r=>r.enabled&&r.gameObject.activeInHierarchy&&GeometryUtility.TestPlanesAABB(planes,r.bounds));
            cameras.Add(new CameraSnapshot{delay=i,camera=camera.transform.position,player=world.player.position,playerInFrustum=visible});
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"actual-save-restored-"+i+"s.png"));
        }
        receipt.cameraSnapshots=cameras.ToArray();yield return null;
        // Main flow owns normal quit/disable autosaves. Unload it first, then clear
        // the isolated QA store; otherwise its quit callback recreates the slot.
        yield return SceneManager.LoadSceneAsync("Assets/SAM/BossArena/Scenes/SAM_BossArena.unity",LoadSceneMode.Single);
        yield return null;yield return null;
        if(mode=="migrate"){
            try{Require(receipt.migrationExpected&&receipt.migrationPersisted,"explicit migrate process verified a real legacy migration");Require(BaselineSaveStore.TryLoad(fixture.prefix,out var durable,out var durableMessage)&&durable.mapVersion==receipt.targetMapVersion,"migrated slot survives main-world unload");Require(durable.safePosition.x==expected.safePosition.x&&durable.safePosition.z==expected.safePosition.z&&durable.safeYaw==expected.safeYaw,"unload keeps approved mapped XZ and yaw");var compare=JsonUtility.FromJson<BaselineSaveData>(JsonUtility.ToJson(durable));compare.safePosition=expected.safePosition;Require(JsonUtility.ToJson(compare)==JsonUtility.ToJson(expected),"unload preserves every non-position progress field");var exported=new Fixture{prefix=fixture.prefix,primary=PlayerPrefs.GetString(fixture.prefix+".primary"),backup=PlayerPrefs.GetString(fixture.prefix+".backup"),sourceReceipt="Original V2 clone migrated by native source flow; next process must read durable V3 without any reseed",extraPrefixes=fixture.extraPrefixes};File.WriteAllText(Path.Combine(output,"migrated-fixture.json"),JsonUtility.ToJson(exported));Finish(null,false);}
            catch(Exception error){Fail(error);}
        }else Finish(null,true);
    }

    void PrepareExpectedMapContract()
    {
        var layout=flow.GetComponent<OverworldV2Layout>();
        Require(layout&&layout.AuthoredMapVersion==3&&Mathf.Abs(layout.PositionScaleFromVersion2-220f/170f)<.000001f,"authored frozen 220/170 map migration contract");
        receipt.targetMapVersion=layout.AuthoredMapVersion;
        string before=JsonUtility.ToJson(expected);
        bool reset=expected.safePositionWasReset;
        expected=JsonUtility.FromJson<BaselineSaveData>(before);
        if(receipt.sourceMapVersion!=3||reset){
            if(receipt.sourceMapVersion==2&&!reset)expected.safePosition=new Vector3(expected.safePosition.x*(220f/170f),expected.safePosition.y,expected.safePosition.z*(220f/170f));
            else{expected.safePosition=layout.CampPosition;expected.safeYaw=0;}
            expected.mapVersion=3;expected.safePositionWasReset=false;
        }
        expected.openingCompleted=true;expected.northBridgeOpen=true;
        receipt.migrationExpected=before!=JsonUtility.ToJson(expected);
    }

    void PrepareMain(Scene scene,LoadSceneMode mode)
    {
        if(scene.path!=Main)return;
        foreach(var root in scene.GetRootGameObjects())foreach(var candidate in root.GetComponentsInChildren<BaselineFlow>(true))
        {candidate.saveKeyPrefix=fixture.prefix;flow=candidate;}
    }
    void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);assertions.Add(message);}
    void Fail(Exception error){Debug.LogException(error);Finish(error.ToString(),true);}
    void Finish(string error,bool cleanup)
    {
        if(complete)return;complete=true;SceneManager.sceneLoaded-=PrepareMain;
        if(receipt==null)receipt=new Receipt{mode=mode};
        if(cleanup&&fixture!=null&&fixture.prefix.StartsWith("QA.Takeover.ActualSave."))
        {
            try{BaselineSaveStore.Clear(fixture.prefix);receipt.qaStoreCleared=!BaselineSaveStore.HasAnyData(fixture.prefix);}
            catch(Exception e){error=(error??"")+"\nQA cleanup: "+e;}
        }
        receipt.error=error;receipt.assertions=assertions.ToArray();receipt.passed=error==null&&(!cleanup||receipt.qaStoreCleared);
        if(output!=null)File.WriteAllText(Path.Combine(output,"actual-save-check.json"),JsonUtility.ToJson(receipt,true));
        Debug.Log("ACTUAL_SAVE_PROCESS "+(receipt.passed?"PASS":"FAIL")+" "+mode+" assertions="+assertions.Count);
        Application.Quit(receipt.passed?0:2);
    }
    static string Sha(string text){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
    static string Arg(string[] args,string key){int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new ArgumentException(key);return args[i+1];}
}

