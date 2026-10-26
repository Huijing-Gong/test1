using UnityEngine;

public enum BattleSpecialSound {Ready,ChargeReady,Shot,Dash,Rise,CounterReady,CounterSuccess,CounterFail,Impact,Wall}

// A very small synthesized shared library: no imported sound pack and only four
// simultaneous sources. Preparation sounds yield to contact/counter feedback.
public sealed class BattleSpecialAudio : MonoBehaviour
{
    public const int VoiceCapacity=4;
    static AudioClip[] clips;
    static int clipOwners;
    bool ownsClips;
    AudioSource[] voices;
    int[] priorities;
    bool[] pausedVoices;
    bool paused;
    bool chargeLoop;
    AudioClip chargeHum;

    void Awake()
    {
        EnsureClips();
        clipOwners++;
        ownsClips=true;
        const int rate=22050;
        var hum=new float[rate/2];
        for(int i=0;i<hum.Length;i++)
        {float t=i/(float)rate;hum[i]=(Mathf.Sin(t*180*Mathf.PI*2)+Mathf.Sin(t*360*Mathf.PI*2)*.18f)*.12f;}
        chargeHum=AudioClip.Create("K gathering hum",hum.Length,1,rate,false);chargeHum.SetData(hum,0);
        voices=new AudioSource[VoiceCapacity];priorities=new int[VoiceCapacity];pausedVoices=new bool[VoiceCapacity];
        for(int i=0;i<VoiceCapacity;i++)
        {
            var voice=new GameObject("Bunny K audio "+i);voice.transform.SetParent(transform,false);
            voices[i]=voice.AddComponent<AudioSource>();
            voices[i].playOnAwake=false;voices[i].loop=false;voices[i].spatialBlend=0;
            voices[i].volume=.22f;voices[i].priority=100;
        }
    }

    public void Play(BattleSpecialSound sound,float power=0)
    {
        if(voices==null || !isActiveAndEnabled || Time.timeScale<=0)return;
        if(sound==BattleSpecialSound.CounterReady && SideAAudio.GuardRaise())return;
        if(sound==BattleSpecialSound.CounterFail && SideAAudio.CounterFail())return;
        // Counter success is reported once by the resolved block in TakeHit.
        if(sound==BattleSpecialSound.CounterSuccess && SideAAudio.Available)return;
        int importance=sound==BattleSpecialSound.CounterSuccess ? 3 :
            sound==BattleSpecialSound.Impact || sound==BattleSpecialSound.Shot ? 2 :
            sound==BattleSpecialSound.Ready || sound==BattleSpecialSound.ChargeReady || sound==BattleSpecialSound.CounterReady ? 0 : 1;
        int chosen=-1;
        for(int i=chargeLoop?1:0;i<voices.Length;i++) if(!voices[i].isPlaying){chosen=i;break;}
        if(chosen<0)
            for(int i=chargeLoop?1:0;i<voices.Length;i++)
                if(priorities[i]<=importance && (chosen<0 || priorities[i]<priorities[chosen])) chosen=i;
        if(chosen<0)return;
        var voice=voices[chosen];voice.Stop();voice.clip=clips[(int)sound];
        voice.pitch=1f+Mathf.Clamp01(power)*.08f;
        voice.volume=sound==BattleSpecialSound.CounterFail ? .12f : importance==0 ? .16f : .24f;
        priorities[chosen]=importance;voice.Play();
    }

    void Update()
    {
        bool shouldPause=Time.timeScale<=0;
        if(paused==shouldPause || voices==null)return;
        paused=shouldPause;
        for(int i=0;i<voices.Length;i++)
        {
            if(paused) {pausedVoices[i]=voices[i].isPlaying;if(pausedVoices[i]) voices[i].Pause();}
            else if(pausedVoices[i]) {voices[i].UnPause();pausedVoices[i]=false;}
        }
    }

    public void Stop()
    {
        StopCharge();
        if(voices==null)return;
        foreach(var voice in voices)if(voice)voice.Stop();
        System.Array.Clear(pausedVoices,0,pausedVoices.Length);paused=false;
    }
    void OnDisable()=>Stop();
    void OnDestroy()
    {
        Stop();
        if(chargeHum)Destroy(chargeHum);
        if(!ownsClips)return;
        ownsClips=false;clipOwners=Mathf.Max(0,clipOwners-1);
        if(clipOwners==0)DestroySharedClips();
    }

    public void SetCharge(float power)
    {
        if(voices==null || Time.timeScale<=0)return;
        var voice=voices[0];
        if(!chargeLoop){voice.Stop();voice.clip=chargeHum;voice.loop=true;voice.Play();chargeLoop=true;}
        voice.pitch=Mathf.Lerp(.8f,1.5f,power);voice.volume=Mathf.Lerp(.10f,.22f,power);
    }
    public void StopCharge()
    {
        if(!chargeLoop || voices==null)return;
        voices[0].Stop();voices[0].loop=false;pausedVoices[0]=false;chargeLoop=false;
    }

    static void EnsureClips()
    {
        if(clips!=null)return;
        clips=new AudioClip[10];
        const int sampleRate=22050;
        for(int kind=0;kind<clips.Length;kind++)
        {
            var sound=(BattleSpecialSound)kind;
            float duration=sound==BattleSpecialSound.Rise ? .30f : sound==BattleSpecialSound.Shot ? .28f : sound==BattleSpecialSound.CounterSuccess ? .26f : .17f;
            int samples=Mathf.CeilToInt(duration*sampleRate);var data=new float[samples];
            double phase=0;uint seed=(uint)(kind+1)*74973u;
            for(int i=0;i<samples;i++)
            {
                float t=i/(float)sampleRate,u=t/duration;
                float frequency=sound==BattleSpecialSound.Rise ? 240f+Mathf.Sin(u*Mathf.PI)*600f :
                    sound==BattleSpecialSound.Shot ? Mathf.Lerp(190f,92f,u) :
                    sound==BattleSpecialSound.CounterSuccess ? Mathf.Lerp(1450f,820f,u) :
                    sound==BattleSpecialSound.CounterReady ? 1200f :
                    sound==BattleSpecialSound.ChargeReady ? Mathf.Lerp(310f,740f,u) :
                    sound==BattleSpecialSound.Impact ? Mathf.Lerp(240f,95f,u) :
                    sound==BattleSpecialSound.Wall ? 150f : sound==BattleSpecialSound.CounterFail ? 170f : 440f;
                phase+=frequency/sampleRate*Mathf.PI*2;
                seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;
                float noise=((seed&65535)/32767.5f-1f);
                float wave=(float)System.Math.Sin(phase)+.22f*(float)System.Math.Sin(phase*2.04);
                float noiseAmount=sound==BattleSpecialSound.Dash ? .68f :
                    sound==BattleSpecialSound.Shot ? .46f :
                    sound==BattleSpecialSound.Impact || sound==BattleSpecialSound.Wall ? .40f : .055f;
                float attack=Mathf.Clamp01(t/.007f),decay=Mathf.Pow(1f-u,sound==BattleSpecialSound.Rise ? 1.3f : 2.8f);
                data[i]=(wave*(1-noiseAmount)+noise*noiseAmount)*attack*decay*.30f;
            }
            clips[kind]=AudioClip.Create("Bunny K "+sound,samples,1,sampleRate,false);
            clips[kind].hideFlags=HideFlags.DontSave;clips[kind].SetData(data,0);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSharedClips()
    {
        DestroySharedClips();clipOwners=0;
    }
    static void DestroySharedClips()
    {
        if(clips!=null)foreach(var clip in clips)if(clip)Object.Destroy(clip);
        clips=null;
    }
}
