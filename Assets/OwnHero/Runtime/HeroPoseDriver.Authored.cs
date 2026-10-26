using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        PlayableGraph authoredGraph;
        AnimationMixerPlayable authoredMixer;
        AnimationClipPlayable authoredCurrent,authoredPrevious;
        HeroActionDefinition authoredDefinition,authoredOld;
        float authoredAge,authoredOldAge,authoredBlend,authoredFade;
        float authoredFrameStart,authoredDelta,authoredPlaybackRate=1,authoredOldStart,authoredBlendStart;
        Vector3 authoredRootStart,authoredRootEnd;
        Quaternion authoredRotationStart,authoredRotationEnd;
        bool authoredHasRoot;
        Vector3 authoredAnimatorPosition;Quaternion authoredAnimatorRotation;
        public bool AuthoredActive=>authoredGraph.IsValid();
        public bool AuthoredSwordWindow=>AuthoredActive&&authoredDefinition!=null&&
            authoredDefinition.hitOpen>=0&&CurrentTime>=authoredDefinition.hitOpen-.04f&&CurrentTime<=authoredDefinition.hitClose+.04f;

        void ResetAuthoredPresentation()
        {
            if(authoredGraph.IsValid())authoredGraph.Destroy();
            authoredCurrent=authoredPrevious=default;authoredDefinition=authoredOld=null;
            authoredAge=authoredOldAge=authoredBlend=0;authoredHasRoot=false;
        }
        static double AuthorTime(HeroActionDefinition d,float age)
        {
            float t=d.authoredOffset+Mathf.Max(0,age)*Mathf.Max(.01f,d.authoredRate);
            return d.loop?Mathf.Repeat(t,d.clip.length):Mathf.Min(t,Mathf.Max(0,d.clip.length-.0001f));
        }
        void SampleAuthored(HeroActionDefinition d,float seconds,float delta,float fade,float playback,float start,long instance)
        {
            if(!Ready||!d.clip)return;
            if(!authoredGraph.IsValid())
            {
                ResetPresentation();
                if(!capturedRoot)CaptureRoot();
                authoredAnimatorPosition=animator.transform.localPosition;authoredAnimatorRotation=animator.transform.localRotation;
                animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;
                authoredGraph=PlayableGraph.Create("Hero shared authored presentation");
                authoredGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                authoredMixer=AnimationMixerPlayable.Create(authoredGraph,2);
                AnimationPlayableOutput.Create(authoredGraph,"Authored body and fingers",animator).SetSourcePlayable(authoredMixer);
                authoredGraph.Play();
            }
            bool replay=restartAction==d.id&&CurrentAction==d.id&&authoredCurrent.IsValid();restartAction=null;
            bool changed=replay||authoredDefinition!=d||!authoredCurrent.IsValid()||
                (instance!=0&&CurrentActionInstance!=0&&instance!=CurrentActionInstance);
            float dt=Mathf.Max(0,delta);
            if(changed)
            {
                freezePrevious=replay;
                if(authoredPrevious.IsValid()){authoredGraph.Disconnect(authoredMixer,0);authoredGraph.DestroyPlayable(authoredPrevious);}
                authoredPrevious=authoredCurrent;authoredOld=authoredDefinition;authoredOldAge=authoredAge;
                if(authoredPrevious.IsValid()){authoredGraph.Disconnect(authoredMixer,1);authoredGraph.Connect(authoredPrevious,0,authoredMixer,0);}
                authoredCurrent=AnimationClipPlayable.Create(authoredGraph,d.clip);
                authoredCurrent.SetSpeed(0);authoredCurrent.SetApplyFootIK(d.authoredFootIK);
                authoredCurrent.SetApplyPlayableIK(false);authoredGraph.Connect(authoredCurrent,0,authoredMixer,1);
                authoredBlend=authoredPrevious.IsValid()?0:1;
                authoredFade=Mathf.Max(.025f,fade<0?d.blendDuration:fade);
            }
            authoredDelta=dt;authoredPlaybackRate=Mathf.Max(0,playback);
            authoredFrameStart=float.IsNaN(start)?changed?Mathf.Max(0,seconds-dt*authoredPlaybackRate):authoredAge:start;
            authoredOldStart=authoredOldAge;authoredBlendStart=authoredBlend;
            authoredRootStart=authoredHasRoot?authoredRootEnd:transform.position;
            authoredRotationStart=authoredHasRoot?authoredRotationEnd:transform.rotation;
            authoredRootEnd=transform.position;authoredRotationEnd=transform.rotation;authoredHasRoot=true;
            authoredDefinition=d;authoredAge=Mathf.Max(0,seconds);if(!freezePrevious)authoredOldAge+=dt;
            authoredBlend=Mathf.Min(1,authoredBlend+dt/authoredFade);
            EvaluateAuthored(authoredAge,authoredOldAge,authoredBlend);
            transform.localPosition=basePosition;transform.localRotation=baseRotation;
            CurrentAction=d.id;CurrentTime=authoredAge;CurrentActionInstance=instance;
            CommitPose();
        }
        void EvaluateAuthored(float time,float oldTime,float weight)
        {
            authoredCurrent.SetTime(AuthorTime(authoredDefinition,time));
            if(authoredPrevious.IsValid()&&authoredOld!=null)authoredPrevious.SetTime(AuthorTime(authoredOld,oldTime));
            float smooth=Mathf.SmoothStep(0,1,weight);
            authoredMixer.SetInputWeight(0,1-smooth);authoredMixer.SetInputWeight(1,smooth);authoredGraph.Evaluate(0);
            animator.transform.localPosition=authoredAnimatorPosition;animator.transform.localRotation=authoredAnimatorRotation;
            ApplyAuthoredDeathSupport(authoredDefinition,time);
            ApplyAuthoredClimbRoot(authoredDefinition,time,authoredOld,oldTime,smooth);
        }
        bool QueryAuthored(float time,out Vector3 a,out Vector3 b)
        {
            a=WeaponBase;b=WeaponTip;
            if(!WeaponReady||!authoredGraph.IsValid()||authoredDefinition==null||poseBones==null)return false;
            float span=authoredAge-authoredFrameStart;
            if(time<authoredFrameStart-.0001f||time>authoredAge+.0001f)return false;
            if(span<=.000001f||time>=authoredAge-.000001f)return true;
            float f=Mathf.Clamp01((time-authoredFrameStart)/span);
            if(queryPositions==null||queryPositions.Length!=poseBones.Length)
            {queryPositions=new Vector3[poseBones.Length];queryRotations=new Quaternion[poseBones.Length];queryScales=new Vector3[poseBones.Length];}
            for(int i=0;i<poseBones.Length;i++)if(poseBones[i])
            {queryPositions[i]=poseBones[i].localPosition;queryRotations[i]=poseBones[i].localRotation;queryScales[i]=poseBones[i].localScale;}
            Vector3 root=transform.position;Quaternion rotation=transform.rotation;
            try
            {
                EvaluateAuthored(time,Mathf.Lerp(authoredOldStart,authoredOldAge,f),
                    Mathf.Min(1,authoredBlendStart+authoredDelta*f/authoredFade));
                transform.SetPositionAndRotation(Vector3.Lerp(authoredRootStart,authoredRootEnd,f),Quaternion.Slerp(authoredRotationStart,authoredRotationEnd,f));
                a=WeaponBase;b=WeaponTip;return true;
            }
            finally
            {
                authoredCurrent.SetTime(AuthorTime(authoredDefinition,authoredAge));
                if(authoredPrevious.IsValid()&&authoredOld!=null)authoredPrevious.SetTime(AuthorTime(authoredOld,authoredOldAge));
                float w=Mathf.SmoothStep(0,1,authoredBlend);authoredMixer.SetInputWeight(0,1-w);authoredMixer.SetInputWeight(1,w);
                transform.SetPositionAndRotation(root,rotation);
                for(int i=0;i<poseBones.Length;i++)if(poseBones[i])
                {poseBones[i].localPosition=queryPositions[i];poseBones[i].localRotation=queryRotations[i];poseBones[i].localScale=queryScales[i];}
            }
        }
    }
}
