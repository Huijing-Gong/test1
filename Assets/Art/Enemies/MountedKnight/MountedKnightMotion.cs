using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DisallowMultipleComponent]
public sealed class MountedKnightMotion : MonoBehaviour
{
    public Animator animator;
    // Idle, walk, gallop, charge, cleave, combo, stomp, execution, death, retired slot, thrust, warding hoof.
    public AnimationClip[] clips=new AnimationClip[12];
    public float blendSeconds=.18f;
    PlayableGraph graph;AnimationMixerPlayable mixer;
    readonly AnimationClipPlayable[] players=new AnimationClipPlayable[3];
    readonly int[] indices={-1,-1,-1};
    readonly float[] weights=new float[3],from=new float[3];
    int slot=-1,active=-1;float blend;bool suspended;
    public int ActiveClip=>active;
    public double SourceTime=>slot>=0&&players[slot].IsValid()?players[slot].GetTime():0;
    public float WeightSum=>weights[0]+weights[1]+weights[2];
    void Awake(){
        if(!animator)animator=GetComponentInChildren<Animator>();
        graph=PlayableGraph.Create("Mounted knight motion");graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer=AnimationMixerPlayable.Create(graph,3);
        AnimationPlayableOutput.Create(graph,"Knight",animator).SetSourcePlayable(mixer);
        animator.applyRootMotion=false;graph.Play();Play(0);
    }
    public void Play(int index,float speed=1,bool restart=false,float sourceTime=0){
        // Rising riposte is removed, including direct preview/debug playback.
        if(!graph.IsValid()||index<0||index==9||index>=clips.Length||!clips[index])return;
        if(active==index&&!restart){players[slot].SetSpeed(speed);return;}
        int next=-1;float smallest=float.PositiveInfinity;
        for(int i=0;i<3;i++)if(i!=slot&&weights[i]<smallest){smallest=weights[i];next=i;}
        if(next<0)next=0;
        if(players[next].IsValid()){graph.Disconnect(mixer,next);graph.DestroyPlayable(players[next]);}
        weights[next]=0;float sum=weights[0]+weights[1]+weights[2];
        for(int i=0;i<3;i++)from[i]=weights[i]=sum>.0001f?weights[i]/sum:0;
        players[next]=AnimationClipPlayable.Create(graph,clips[index]);players[next].SetApplyFootIK(false);
        players[next].SetTime(sourceTime);players[next].SetSpeed(speed);indices[next]=index;
        graph.Connect(players[next],0,mixer,next);
        bool first=slot<0;slot=next;active=index;blend=0;
        if(first){weights[next]=from[next]=1;blend=blendSeconds;}
        ApplyWeights();
    }
    public void Suspend(bool value){suspended=value;if(graph.IsValid())mixer.SetSpeed(value?0:1);}
    void Update(){
        if(!graph.IsValid()||suspended)return;
        blend+=Time.deltaTime;float t=Mathf.Clamp01(blend/Mathf.Max(.01f,blendSeconds));t=t*t*(3-2*t);
        for(int i=0;i<3;i++){
            weights[i]=Mathf.Lerp(from[i],i==slot?1:0,t);
            if(!players[i].IsValid())continue;
            var clip=clips[indices[i]];double time=players[i].GetTime();
            if(indices[i]<=2&&time>=clip.length)players[i].SetTime(time%clip.length);
            else if(indices[i]>2&&time>clip.length)players[i].SetTime(clip.length);
            if(i!=slot&&weights[i]<.0001f)players[i].SetSpeed(0);
        }
        ApplyWeights();
    }
    void ApplyWeights(){for(int i=0;i<3;i++)mixer.SetInputWeight(i,weights[i]);}
    void OnDestroy(){if(graph.IsValid())graph.Destroy();}
}
