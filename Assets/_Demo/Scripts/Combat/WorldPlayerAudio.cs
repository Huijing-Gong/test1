using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldPlayerAudio:MonoBehaviour
{
    public AudioClip bladeWhoosh;
    [Range(0,1)] public float swingVolume=.38f;
    PlayerCombat combat;AudioSource voice;
    public int SwingCount{get;private set;}
    public AudioClip LastClip{get;private set;}
    void Awake()
    {
        combat=GetComponent<PlayerCombat>();
        var go=new GameObject("World sword swing voice");go.transform.SetParent(transform,false);
        voice=go.AddComponent<AudioSource>();voice.playOnAwake=false;voice.spatialBlend=.35f;
        voice.dopplerLevel=0;voice.minDistance=2;voice.maxDistance=18;
    }
    void OnEnable(){if(!combat)combat=GetComponent<PlayerCombat>();if(combat)combat.SwordSwingStarted+=Swing;}
    void OnDisable(){if(combat)combat.SwordSwingStarted-=Swing;if(voice)voice.Stop();}
    void Swing(){if(!bladeWhoosh||!voice)return;voice.pitch=Random.Range(.97f,1.03f);voice.PlayOneShot(bladeWhoosh,swingVolume);LastClip=bladeWhoosh;SwingCount++;}
}
