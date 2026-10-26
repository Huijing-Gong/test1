using System.Collections.Generic;
using UnityEngine;

// One short, spatialised non-verbal voice per creature; physical weapon/impact
// sounds remain in the existing shared enemy pool. No speech or idle voice loop.
[DisallowMultipleComponent,RequireComponent(typeof(Health))]
public sealed class AdventureCreatureVoice : MonoBehaviour
{
    static readonly HashSet<AdventureCreatureVoice> talking=new HashSet<AdventureCreatureVoice>();
    static readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
    Health health; AdventureCreatureAI creature; DemonKingCombatVisual demon;
    EnemyAwareness awareness; PlayerVitals player; BaselineFlow flow;
    AudioSource voice; string species;
    int confirmations,swings,variant;
    float attackDelay=-1,nextAlert,nextHurt,searchAt;
    string queuedReaction;
    float reactionGain;
    bool paused;
    public int PlayedCount {get;private set;}
    public string LastEvent {get;private set;}
    public bool VoicePlaying=>voice&&voice.isPlaying;
    public AudioSource Source=>voice;
    public static AdventureCreatureVoice Ensure(GameObject root)
    {var found=root.GetComponent<AdventureCreatureVoice>();return found?found:root.AddComponent<AdventureCreatureVoice>();}
    void Awake()
    {
        health=GetComponent<Health>();creature=GetComponent<AdventureCreatureAI>();demon=GetComponentInChildren<DemonKingCombatVisual>();
        species=creature is FishmanAI?"Fishman":creature is SmilingMushroomAI?"Mushroom":creature is BishopKnightAI?"Knight":"DemonKing";
        awareness=GetComponent<EnemyAwareness>();
        voice=gameObject.AddComponent<AudioSource>();voice.playOnAwake=false;voice.loop=false;voice.spatialBlend=1;
        voice.rolloffMode=AudioRolloffMode.Linear;voice.minDistance=2;voice.maxDistance=22;voice.dopplerLevel=0;voice.priority=100;voice.reverbZoneMix=.12f;
        FindWorld();
    }
    void OnEnable()
    {health.Damaged+=Hurt;health.Died+=Died;if(flow)flow.WorldEncountersReset+=ResetVoice;}
    void OnDisable()
    {if(health){health.Damaged-=Hurt;health.Died-=Died;}if(flow)flow.WorldEncountersReset-=ResetVoice;ResetVoice();}
    void FindWorld()
    {
        searchAt=Time.unscaledTime+1;
        foreach(var p in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))if(p.gameObject.scene==gameObject.scene){player=p;break;}
        foreach(var host in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))if(host.gameObject.scene==gameObject.scene){flow=host;break;}
    }
    void Update()
    {
        if(!player&&Time.unscaledTime>=searchAt)FindWorld();
        bool active=player&&player.EnemyCombatActive&&player.gameObject.scene==gameObject.scene;
        if(!active||Time.timeScale<=0)
        {
            if(EnemyAwareness.CanRetainWorld(player,flow))
            {if(voice.isPlaying){voice.Pause();paused=true;}}
            else ResetVoice();
            return;
        }
        if(paused){paused=false;voice.UnPause();}
        if(!voice.isPlaying)talking.Remove(this);
        // Health events later in a multi-target sword sweep can occur during
        // its owned hit-stop. Keep the strongest reaction until simulation resumes.
        if(queuedReaction!=null)
        {var cue=queuedReaction;queuedReaction=null;Play(cue,reactionGain);}
        if(health.IsDead)return;
        if(!awareness)awareness=GetComponent<EnemyAwareness>();
        if(awareness&&awareness.ConfirmationCount!=confirmations)
        {
            confirmations=awareness.ConfirmationCount;
            // A first strike can also confirm the target. Do not replace its
            // fresh pain reaction with a lower-priority discovery grunt.
            bool reacting=voice.isPlaying&&LastEvent!=null&&LastEvent.StartsWith("Hurt");
            if(awareness.Engaged&&Time.time>=nextAlert&&!reacting){nextAlert=Time.time+6;Play("Alert1",.50f);}
        }
        int current=creature?creature.Swings:demon?demon.SwingCount:0;
        bool attacking=creature?creature.CurrentAction!=null&&creature.CurrentAction.StartsWith("Attack"):demon&&demon.AttackElapsed>=0;
        if(current!=swings)
        {swings=current;attackDelay=creature?creature.VoiceAttackDelay:demon?Mathf.Max(0,demon.contactStart-.10f):0;}
        if(!attacking)attackDelay=-1;
        if(attackDelay>=0)
        {attackDelay-=Time.deltaTime;if(attackDelay<=0){attackDelay=-1;Play((variant++&1)==0?"Attack1":"Attack2",.62f);}}
    }
    void Hurt(Health target,DamageInfo damage)
    {
        attackDelay=-1;if(target.IsDead||Time.time<nextHurt)return;
        nextHurt=Time.time+.18f;Reaction((variant++&1)==0?"Hurt1":"Hurt2",.56f);
    }
    void Died(Health target,DamageInfo damage){attackDelay=-1;Reaction("Death1",.62f);}
    void Reaction(string cue,float gain)
    {
        if(!player||!player.EnemyCombatActive)return;
        if(Time.timeScale<=0){if(queuedReaction!="Death1"){queuedReaction=cue;reactionGain=gain;}return;}
        Play(cue,gain);
    }
    void Play(string name,float volume)
    {
        if(!player||!player.EnemyCombatActive||Time.timeScale<=0)return;
        var camera=Camera.main;
        if(camera&&Vector3.Distance(camera.transform.position,transform.position)>voice.maxDistance)return;
        string path="CreatureVoices/"+species+"/"+name;
        if(!clips.TryGetValue(path,out var clip)||!clip){clip=Resources.Load<AudioClip>(path);clips[path]=clip;}
        if(!clip)return;
        talking.RemoveWhere(item=>!item||!item.voice||(!item.voice.isPlaying&&!item.paused));
        if(talking.Count>=4&&!talking.Contains(this))return;
        paused=false;voice.Stop();voice.clip=clip;voice.volume=volume;voice.pitch=1f;voice.Play();talking.Add(this);
        PlayedCount++;LastEvent=name;
    }
    void ResetVoice()
    {attackDelay=-1;queuedReaction=null;paused=false;nextAlert=nextHurt=0;if(voice)voice.Stop();talking.Remove(this);}
}
