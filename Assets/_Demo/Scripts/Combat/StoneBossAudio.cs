using UnityEngine;
using KingOfGraphic.MechBossMigration;

[DisallowMultipleComponent,RequireComponent(typeof(MechSentinelBattle))]
public sealed class StoneBossAudio:MonoBehaviour
{
    public AudioClip stompImpact;
    [Range(0,1)] public float impactVolume=.62f;
    MechSentinelBattle battle;AudioSource voice;
    public int ImpactCount{get;private set;}
    public Vector3 LastImpactPosition{get;private set;}
    public float LastImpactAttackTime{get;private set;}
    void Awake()
    {
        battle=GetComponent<MechSentinelBattle>();
        var go=new GameObject("Sentinel ground impact voice");go.transform.SetParent(transform,false);
        voice=go.AddComponent<AudioSource>();voice.playOnAwake=false;voice.spatialBlend=.9f;
        voice.dopplerLevel=0;voice.minDistance=3;voice.maxDistance=32;
    }
    void OnEnable(){if(!battle)battle=GetComponent<MechSentinelBattle>();battle.GroundImpact+=Impact;}
    void OnDisable(){if(battle)battle.GroundImpact-=Impact;if(voice)voice.Stop();}
    void Impact(Vector3 point)
    {
        if(!voice||!stompImpact)return;
        voice.transform.position=point;voice.pitch=Random.Range(.97f,1.02f);voice.PlayOneShot(stompImpact,impactVolume);
        LastImpactPosition=point;LastImpactAttackTime=battle.AttackElapsed;ImpactCount++;
    }
}
