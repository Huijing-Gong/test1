using System;
using System.Collections.Generic;
using UnityEngine;

// Short, authored scenic sequence on the existing camera. No second gameplay camera/Brain.
// The route stays editable independently of the terrain and uses unscaled time while Flow pauses play.
public sealed class BaselineOpeningCamera : MonoBehaviour
{
    [Serializable] public struct Shot
    {
        public float time;
        public Vector3 position, focus;
        public Shot(float t, Vector3 p, Vector3 f) { time=t;position=p;focus=f; }
        public Pose Pose => new Pose(position,Quaternion.LookRotation(focus-position,Vector3.up));
    }
    [SerializeField] Shot[] shots = {
        new Shot(1.5f,new Vector3(-3,3.6f,-67),new Vector3(-4,2,-61)),
        new Shot(4.5f,new Vector3(-3,8,-39),new Vector3(-46,3,-12)),
        new Shot(6,new Vector3(-2,8.2f,-36),new Vector3(-38,3,-4)),
        new Shot(8,new Vector3(0,8.5f,-34),new Vector3(2,3,4)),
        new Shot(9.5f,new Vector3(3,8,-36),new Vector3(43,4,12)),
        new Shot(11.5f,new Vector3(3,8,-36),new Vector3(3,4,25))
    };
    Shot[] activeShots;
    bool[] fadeBetweenShots;
    public IReadOnlyList<Shot> PlannedShots => activeShots ?? shots;
    public const float Duration=14;
    public bool IsPlaying { get; private set; }
    public float Elapsed { get; private set; }
    public bool UsesEntryFade { get; private set; }
    public Pose EntryPose { get; private set; }
    public float EntryFov { get; private set; }
    Camera view;
    ThirdPersonCamera follow;
    BaselineFlow flow;
    Action finished;
    readonly List<Renderer> hiddenLabels = new List<Renderer>();
    readonly List<Animator> actors = new List<Animator>();
    readonly List<AnimatorUpdateMode> actorModes = new List<AnimatorUpdateMode>();
    GUIStyle subtitle, hint;
    float fade;
    float tourFov;
    public bool Play(BaselineFlow owner, Action onFinished, Transform companion = null)
    {
        if(IsPlaying || !owner || !owner.Started || owner.MenuOpen || owner.VictoryOpen || shots==null || shots.Length==0)return false;
        view=Camera.main;if(!view)return false;
        follow=view.GetComponent<ThirdPersonCamera>();if(!follow||follow.CinematicControlled)return false;
        var layout = owner.GetComponent<OverworldV2Layout>();
        activeShots = layout ? CreateV2Shots(layout, companion) : shots;
        fadeBetweenShots = new bool[activeShots.Length];
        if (layout)
            for (int i=1;i<activeShots.Length;i++)
                fadeBetweenShots[i] = !ClearSegment(activeShots[i-1].position, activeShots[i].position);
        flow=owner;finished=onFinished;
        EntryPose=new Pose(view.transform.position,view.transform.rotation);EntryFov=view.fieldOfView;
        tourFov=layout ? Mathf.Min(EntryFov,50f) : EntryFov;
        UsesEntryFade=!ClearSegment(EntryPose.position,activeShots[0].position);
        Elapsed=0;fade=0;IsPlaying=true;
        follow.SetCinematicControl(true);flow.HoldTutorialCard();
        actors.Clear();actorModes.Clear();
        var playerAnimator=owner.GetComponent<BaselineWorld>().player.GetComponentInChildren<Animator>();
        AnimateActor(playerAnimator);
        if(playerAnimator)playerAnimator.SetFloat("MoveAmount",0);
        if(companion)AnimateActor(companion.GetComponentInChildren<Animator>());
        hiddenLabels.Clear();
        foreach(var text in FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
        {
            if(text.gameObject.scene!=gameObject.scene)continue;
            var renderer=text.GetComponent<Renderer>();
            if(renderer&&renderer.enabled){hiddenLabels.Add(renderer);renderer.enabled=false;}
        }
        return true;
    }
    // One introduction to the same map: the hub, lake island, tower landmark,
    // then the guide. These do not alter player movement or the original scene shots.
    static Shot[] CreateV2Shots(OverworldV2Layout layout, Transform companion)
    {
        var camp = layout.CampPosition;
        var island = layout.IslandDestination;
        var guide = companion ? companion.position : camp;
        var towerFocus = new Vector3(61,39,61);
        var owner = layout.GetComponent<BaselineFlow>();
        // Preserve the landmark's horizontal framing while its original entrance
        // follows an elevated tower terrace. Older layouts keep the authored focus.
        if (owner && owner.endingEntrance) towerFocus.y = owner.endingEntrance.position.y + .95f;
        return new[] {
            new Shot(1.5f,camp+new Vector3(3,5,-8),camp+Vector3.up*1.2f),
            new Shot(4.5f,camp+new Vector3(0,11,12),island+Vector3.up*2),
            new Shot(6f,camp+new Vector3(-8,15,20),island+Vector3.up*3),
            new Shot(8f,camp+new Vector3(-5,17,23),towerFocus),
            new Shot(9.5f,camp+new Vector3(2,14,15),towerFocus),
            new Shot(11.5f,guide+new Vector3(4,2.7f,-5),guide+Vector3.up*1.1f)
        };
    }
    void AnimateActor(Animator animator)
    {
        if(!animator)return;
        actors.Add(animator);actorModes.Add(animator.updateMode);
        animator.updateMode=AnimatorUpdateMode.UnscaledTime;
    }
    // Bounded radius check is also used by the editor shot review.
    public static bool ClearSegment(Vector3 a,Vector3 b)
    {
        if(Physics.CheckSphere(a,.32f,~0,QueryTriggerInteraction.Ignore)||Physics.CheckSphere(b,.32f,~0,QueryTriggerInteraction.Ignore))return false;
        var delta=b-a;
        return delta.sqrMagnitude<.00001f || !Physics.SphereCast(a,.32f,delta.normalized,out _,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
    }
    public Pose Evaluate(float seconds, Pose entry, bool entryFade, out float black)
    {
        var sequence = activeShots ?? shots;
        black=0;
        if(seconds<1.5f)
        {
            if(entryFade)
            {
                black=seconds<.45f?Mathf.SmoothStep(0,1,seconds/.45f):1-Mathf.SmoothStep(0,1,(seconds-.65f)/.65f);
                return seconds<.55f?entry:sequence[0].Pose;
            }
            return Blend(entry,sequence[0].Pose,seconds/1.5f);
        }
        for(int i=1;i<sequence.Length;i++)
            if(seconds<sequence[i].time)
            {
                float t=(seconds-sequence[i-1].time)/(sequence[i].time-sequence[i-1].time);
                // Existing vegetation may obstruct the flight between two safe shots.
                // Cut under black in that case instead of flying through its collider.
                if (fadeBetweenShots != null && fadeBetweenShots.Length > i && fadeBetweenShots[i])
                {
                    black = t < .5f ? Mathf.SmoothStep(0,1,Mathf.InverseLerp(.25f,.5f,t)) :
                        1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.5f,.75f,t));
                    return t < .5f ? sequence[i-1].Pose : sequence[i].Pose;
                }
                return Blend(sequence[i-1].Pose,sequence[i].Pose,t);
            }
        var last=sequence[sequence.Length-1].Pose;
        if(seconds<12)return last;
        // Camera returns under full black; no reverse flight over the map.
        if(seconds<12.55f){black=Mathf.SmoothStep(0,1,(seconds-12)/.55f);return last;}
        if(seconds<12.75f){black=1;return entry;}
        black=1-Mathf.SmoothStep(0,1,(seconds-12.75f)/.65f);return entry;
    }
    static Pose Blend(Pose a,Pose b,float t)
    {
        t=Mathf.SmoothStep(0,1,Mathf.Clamp01(t));
        return new Pose(Vector3.Lerp(a.position,b.position,t),Quaternion.Slerp(a.rotation,b.rotation,t));
    }
    void LateUpdate()
    {
        if(!IsPlaying)return;
        if(!flow || !flow.Started || !view || !follow){Abort();return;}
        Elapsed+=Time.unscaledDeltaTime;
        var pose=Evaluate(Elapsed,EntryPose,UsesEntryFade,out fade);
        view.transform.SetPositionAndRotation(pose.position,pose.rotation);
        // Keep the western hidden chest outside the V2 lake establishing shot.
        // Restore the normal FOV under the existing return fade, not after it.
        view.fieldOfView=Elapsed>=12.55f ? EntryFov : Mathf.Lerp(EntryFov,tourFov,Mathf.SmoothStep(0,1,Elapsed/1.5f));
        if(Elapsed>=Duration)Complete();
    }
    public void Complete()
    {
        if(!IsPlaying)return;
        var callback=finished;Restore();callback?.Invoke();
    }
    public void Abort() { if(IsPlaying)Restore(); }
    void Restore()
    {
        IsPlaying=false;finished=null;fade=0;
        if(view){view.fieldOfView=EntryFov;view.transform.SetPositionAndRotation(EntryPose.position,EntryPose.rotation);}
        if(follow)follow.SetCinematicControl(false);
        foreach(var renderer in hiddenLabels)if(renderer)renderer.enabled=true;
        hiddenLabels.Clear();
        for(int i=0;i<actors.Count;i++)if(actors[i])actors[i].updateMode=actorModes[i];
        actors.Clear();actorModes.Clear();
        if(flow)flow.ReleaseTutorialCard();
    }
    void OnGUI()
    {
        WayfarerUITheme.Apply();
        if(!IsPlaying)return;
        var matrix=GUI.matrix;var color=GUI.color;int depth=GUI.depth;
        GUI.matrix=Matrix4x4.identity;GUI.depth=-100;
        float scale=Mathf.Min(Screen.width/960f,Screen.height/600f);
        float bars=Screen.height*.085f;
        GUI.color=new Color(0,0,0,Mathf.Clamp01(Elapsed/.4f));
        GUI.DrawTexture(new Rect(0,0,Screen.width,bars),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0,Screen.height-bars,Screen.width,bars),Texture2D.whiteTexture);
        if(subtitle==null){subtitle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,wordWrap=true};hint=new GUIStyle(subtitle);}
        subtitle.fontSize=Mathf.RoundToInt(18*scale);hint.fontSize=Mathf.RoundToInt(13*scale);
        GUI.color=Color.white;
        if(Elapsed<4.5f)GUI.Label(new Rect(Screen.width*.15f,Screen.height-bars-52*scale,Screen.width*.7f,48*scale),
            "Take a look. There is more to this lake than meets the eye.",subtitle);
        GUI.Label(new Rect(20,Screen.height-bars,Screen.width-40,bars),"Esc: skip view    |    Tab: skip introduction",hint);
        if(GUI.Button(new Rect(Screen.width-150*scale,18*scale,130*scale,30*scale),"Skip view [Esc]"))Complete();
        if(fade>0){GUI.color=new Color(0,0,0,fade);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);}
        GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;
    }
    void OnDisable(){Abort();}
    void OnDestroy(){Abort();}
}
