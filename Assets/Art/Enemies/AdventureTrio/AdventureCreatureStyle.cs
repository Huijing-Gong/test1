using UnityEngine;

// Presentation only: does not advance combat, own materials, or alter native rigs.
[DisallowMultipleComponent, RequireComponent(typeof(AdventureCreatureAI), typeof(Health))]
public sealed class AdventureCreatureStyle : MonoBehaviour
{
    public enum Species { Fishman, Mushroom, Knight }
    public Species species;
    public Renderer[] targetRenderers;
    public float StyleClock { get; private set; }
    public float TechPulse { get; private set; }
    static readonly int ClockId=Shader.PropertyToID("_StyleClock");
    static readonly int PulseId=Shader.PropertyToID("_TechPulse");
    static readonly int LiveId=Shader.PropertyToID("_StyleLive");
    AdventureCreatureAI creature;
    Health health;
    PlayerVitals player;
    Health playerHealth;
    BaselineFlow flow;
    MaterialPropertyBlock block;
    float searchAt;
    bool listening;
    void Awake()
    {
        creature=GetComponent<AdventureCreatureAI>(); health=GetComponent<Health>();
        block=new MaterialPropertyBlock(); FindWorld();
    }
    void OnEnable()
    {
        listening=true;
        health.Died+=Died;
        if(flow)flow.WorldEncountersReset+=ResetForJourney;
        Apply(health.IsDead?0:1);
    }
    void FindWorld()
    {
        searchAt=Time.unscaledTime+1;
        foreach(var p in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
            if(p.gameObject.scene==gameObject.scene){player=p;break;}
        playerHealth=player?player.GetComponent<Health>():null;
        if(flow)return;
        foreach(var host in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if(host.gameObject.scene==gameObject.scene)
            {flow=host;if(listening)flow.WorldEncountersReset+=ResetForJourney;break;}
    }
    void LateUpdate()
    {
        if((!player||!flow)&&Time.unscaledTime>=searchAt)FindWorld();
        if(health.IsDead||!creature.isActiveAndEnabled){TechPulse=0;Apply(0);return;}
        bool active=player&&playerHealth&&!playerHealth.IsDead&&player.gameObject.scene==gameObject.scene&&player.EnemyCombatActive&&Time.timeScale>0;
        if(!active)
        {
            if(!EnemyAwareness.CanRetainWorld(player,flow))ResetForJourney();
            return;
        }
        StyleClock+=Time.deltaTime;
        TechPulse=Mathf.MoveTowards(TechPulse,creature.Anticipation01,Time.deltaTime*5);
        Apply(1);
    }
    void Died(Health target,DamageInfo damage){TechPulse=0;Apply(0);}
    public void ResetForJourney(){StyleClock=0;TechPulse=0;Apply(0);}
    void Apply(float live)
    {
        if(targetRenderers==null||block==null)return;
        foreach(var renderer in targetRenderers)
        {
            if(!renderer)continue;
            renderer.GetPropertyBlock(block);
            block.SetFloat(ClockId,StyleClock);block.SetFloat(PulseId,TechPulse);block.SetFloat(LiveId,live);
            renderer.SetPropertyBlock(block);
        }
    }
    void OnDisable()
    {
        listening=false;
        if(health)health.Died-=Died;
        if(flow)flow.WorldEncountersReset-=ResetForJourney;
        ResetForJourney();
    }
}
