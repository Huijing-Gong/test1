using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Presentation follows durable trial progress, never trial attempts or reward claims.
// Runs on the suspended host root, so side-game lighting and cameras remain independent.
[DefaultExecutionOrder(400)]
public sealed class BaselineEclipseSequence : MonoBehaviour
{
    static readonly int WindowDarknessId=Shader.PropertyToID("_OverworldEclipseDarkness");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetWindowDarkness() => Shader.SetGlobalFloat(WindowDarknessId,0);
    public bool IsPlaying { get; private set; }
    public float VisualStage { get; private set; }
    public static int CompletedStage(BaselineSaveData data) => data == null ? 0 :
        (data.trialACompleted ? 1 : 0) + (data.trialBCompleted ? 1 : 0);

    BaselineFlow flow;
    BaselineWorld world;
    BaselineSaveData progress;
    Camera view;
    ThirdPersonCamera follow;
    Light sun;
    BaselineEclipseLamps lamps;
    Material originalSky, eclipseSky;
    Color sunColor, ambientSky, ambientEquator, ambientGround, fogColor;
    float sunIntensity, ambientIntensity, reflectionIntensity, fogDensity;
    bool originalFog;
    FogMode originalFogMode;
    UnityEngine.Rendering.AmbientMode originalAmbientMode;
    Quaternion sunRotation;
    bool captured;
    Pose entry, skyShot, towerShot, towerEnd, lampShot;
    Vector3 towerOrbitFocus,towerOrbitOffset;
    float towerSweep;
    bool hasLampShot;
    Pose monsterShot, monsterEnd;
    bool hasMonsterShot;
    MeshRenderer[] shotScenery;
    Health cinematicMonster;
    Vector3 monsterOrigin, monsterFocus;
    sealed class MonsterCloseup
    {
        public Health actor;
        public Pose start,end,lastClear;
        public Vector3 origin,focus;
    }
    readonly List<MonsterCloseup> monsterCloseups=new List<MonsterCloseup>();
    Pose lastMonsterPose;
    bool hasLastMonsterPose;
    float entryFov, elapsed, fade, fromStage, clock;
    int targetStage;
#if UNITY_EDITOR
    int previewStage = -1;
    bool previewRequested;
    bool capturedSky, capturedTower;
    public void PreviewStage(int stage,bool preserveCurrentStage=false)
    {
        if(!HostAvailable || IsPlaying) { Debug.LogWarning("Enter the overworld before previewing an eclipse."); return; }
        previewStage=Mathf.Clamp(stage,0,2); previewRequested=previewStage>0;
        if(flow.MenuOpen) flow.SetMenu(false);
        if(world.JournalOpen) world.SetJournal(false);
        if(!preserveCurrentStage) VisualStage=previewStage==2?1:0;
        capturedSky=capturedTower=false;
    }
#endif
    GUIStyle subtitle, caption;
    Vector3 skyDirection;
    float Duration => targetStage == 2 ? 22f : 12.5f;

    void Start()
    {
        flow=GetComponent<BaselineFlow>(); world=GetComponent<BaselineWorld>();
        lamps=GetComponent<BaselineEclipseLamps>();
        if(!lamps) lamps=gameObject.AddComponent<BaselineEclipseLamps>();
        CaptureEnvironment();
    }

    void CaptureEnvironment()
    {
        originalSky=RenderSettings.skybox;
        ambientSky=RenderSettings.ambientSkyColor; ambientEquator=RenderSettings.ambientEquatorColor;
        ambientGround=RenderSettings.ambientGroundColor; ambientIntensity=RenderSettings.ambientIntensity;
        reflectionIntensity=RenderSettings.reflectionIntensity;
        fogColor=RenderSettings.fogColor; fogDensity=RenderSettings.fogDensity;
        originalFog=RenderSettings.fog; originalFogMode=RenderSettings.fogMode;
        originalAmbientMode=RenderSettings.ambientMode;
        sun=RenderSettings.sun;
        if (!sun)
            foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if(light.gameObject.scene==gameObject.scene && light.type==LightType.Directional) { sun=light; break; }
        if(sun) { sunIntensity=sun.intensity; sunColor=sun.color; sunRotation=sun.transform.rotation; }
        var shader=Resources.Load<Shader>("ProgressiveEclipse");
        if(shader) eclipseSky=new Material(shader) { name="Progressive eclipse (runtime)" };
        if(eclipseSky)
        {
            var storm=Resources.Load<Texture2D>("BloodMoonStorm");
            if(storm) { eclipseSky.SetTexture("_StormTex",storm); eclipseSky.SetFloat("_HasStorm",1); }
            var twilight=Resources.Load<Texture2D>("EclipseTwilight");
            if(twilight) eclipseSky.SetTexture("_TwilightTex",twilight);
        }
        if(eclipseSky && originalSky && originalSky.HasProperty("_MainTex") && originalSky.GetTexture("_MainTex"))
        {
            eclipseSky.SetTexture("_MainTex",originalSky.GetTexture("_MainTex"));
            eclipseSky.SetFloat("_HasPanorama",1);
            foreach(string property in new[]{"_Exposure","_Rotation"})
                if(originalSky.HasProperty(property)) eclipseSky.SetFloat(property,originalSky.GetFloat(property));
            if(originalSky.HasProperty("_Tint")) eclipseSky.SetColor("_Tint",originalSky.GetColor("_Tint"));
        }
        captured=true;
    }

    bool HostAvailable => flow && flow.Started && world && world.player &&
        !(SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy)) &&
        !(flow.SamBridge && flow.SamBridge.IsAway) && !(flow.endingRoom && flow.endingRoom.IsInside);

    void Update()
    {
        if(!captured || !flow) return;
        if(progress!=flow.Progress)
        {
            Abort(); progress=flow.Progress;
            VisualStage=Mathf.Min(CompletedStage(progress), Mathf.Clamp(progress.eclipsePresentedStage,0,2));
        }
        if(!HostAvailable) return;
        clock+=Time.unscaledDeltaTime;
        int completed=CompletedStage(progress);
#if UNITY_EDITOR
        if(previewStage>=0)
        {
            if(previewRequested && !flow.BlockWorldInput && !flow.WorldHudHidden && Time.timeScale>0)
            { Begin(previewStage); previewRequested=!IsPlaying; }
            if(!IsPlaying) { VisualStage=previewStage; ApplyEnvironment(VisualStage); return; }
        }
#endif
        if(!IsPlaying && completed>Mathf.Clamp(progress.eclipsePresentedStage,0,2) &&
            !flow.BlockWorldInput && !flow.WorldHudHidden && Time.timeScale>0 && flow.endingEntrance)
            Begin(completed);
        if(IsPlaying)
        {
            elapsed+=Time.unscaledDeltaTime;
            VisualStage=Mathf.Lerp(fromStage,targetStage,Mathf.SmoothStep(0,1,elapsed/5f));
            if(elapsed>=Duration || (elapsed>1 && Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame))
                Finish();
        }
        else VisualStage=Mathf.MoveTowards(VisualStage,Mathf.Min(completed,progress.eclipsePresentedStage),Time.unscaledDeltaTime*.18f);
        ApplyEnvironment(VisualStage);
    }

    void Begin(int stage)
    {
        view=Camera.main; if(!view) return;
        follow=view.GetComponent<ThirdPersonCamera>();
        if(!follow || follow.CinematicControlled) return;
        targetStage=stage; fromStage=stage==2?1:VisualStage; elapsed=fade=0;
        entry=new Pose(view.transform.position,view.transform.rotation); entryFov=view.fieldOfView;
        Vector3 tower=flow.endingEntrance.position;
        Vector3 heading=Vector3.ProjectOnPlane(tower-world.player.position,Vector3.up).normalized;
        if(heading.sqrMagnitude<.01f) heading=Vector3.forward;
        skyDirection=(heading+Vector3.up*.65f).normalized;
        // Skyboxes are infinitely distant: collider-only checks miss foliage and other
        // decorative meshes. Start above the rendered scenery, including colliderless trees.
        var scenery=FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        shotScenery=scenery;
        float skyline=entry.position.y;
        foreach(var renderer in scenery)
            if(IsScenery(renderer)) skyline=Mathf.Max(skyline,renderer.bounds.max.y);
        Vector3 skyCamera=new Vector3(entry.position.x,skyline+8,entry.position.z);
        skyShot=new Pose(skyCamera,Quaternion.LookRotation(skyDirection));
        // Lift the tower shot until the full framing cone is clear, not only its centre ray.
        Vector3 towerCamera=tower-heading*22+Vector3.up*16;
        if(Physics.Raycast(towerCamera+Vector3.up*150,Vector3.down,out var hit,300,~0,QueryTriggerInteraction.Ignore))
            towerCamera.y=Mathf.Max(towerCamera.y,hit.point.y+8);
        Vector3 towerFocus=tower+Vector3.up*3;
        while(towerCamera.y<skyline+12 && ShotBlocked(towerCamera,towerFocus,scenery))
            towerCamera.y+=3;
        towerShot=new Pose(towerCamera,Quaternion.LookRotation(tower+Vector3.up*3-towerCamera));
        towerEnd=towerShot;
        towerOrbitFocus=towerFocus;
        // Always retain a real moving fallback; a failed visibility search must
        // never silently set the orbit angle to zero.
        towerSweep=-50;
        Vector3 horizontal=Vector3.ProjectOnPlane(towerCamera-towerFocus,Vector3.up).normalized*28;
        towerOrbitOffset=Quaternion.AngleAxis(25,Vector3.up)*(horizontal+Vector3.up*Mathf.Max(20,skyline+15-towerFocus.y));
        for(int lift=0;lift<=12;lift++)
        {
            Vector3 offset=Quaternion.AngleAxis(25,Vector3.up)*(horizontal+Vector3.up*(20+lift*3));
            bool clear=true;
            for(int sample=0;sample<=32;sample++)
                if(TowerShotBlocked(towerFocus+Quaternion.AngleAxis(towerSweep*sample/32f,Vector3.up)*offset,towerFocus,scenery))
                { clear=false; break; }
            if(!clear) continue;
            towerOrbitOffset=offset; break;
        }
        Vector3 orbitStart=towerFocus+towerOrbitOffset;
        towerShot=new Pose(orbitStart,Quaternion.LookRotation(towerFocus-orbitStart));
        monsterCloseups.Clear();
        hasLastMonsterPose=false;
        for(int shot=0;shot<2 && FindMonsterShot(scenery);shot++)
            monsterCloseups.Add(new MonsterCloseup { actor=cinematicMonster,start=monsterShot,end=monsterEnd,lastClear=monsterShot,
                origin=monsterOrigin,focus=monsterFocus });
        hasMonsterShot=monsterCloseups.Count>0;
        hasLampShot=false;
        float lampDistance=float.PositiveInfinity;
        foreach(var renderer in scenery)
        {
            if(!IsScenery(renderer) || renderer.name!="street_lamp_3d_model") continue;
            Vector3 focus=renderer.bounds.center;
            Vector3 position=focus+new Vector3(4,1,-6);
            float distance=Vector3.Distance(entry.position,focus);
            if(distance>=lampDistance || ShotBlocked(position,focus,scenery)) continue;
            lampDistance=distance; hasLampShot=true;
            lampShot=new Pose(position,Quaternion.LookRotation(focus-position));
        }
        IsPlaying=true;
        flow.HoldTutorialCard(); follow.SetCinematicControl(true);
        Cursor.visible=false;
#if UNITY_EDITOR
        Debug.Log($"Eclipse sequence started: stage {stage}; Tower {tower}; camera {towerCamera}.");
#endif
    }

    bool IsScenery(MeshRenderer renderer)
    {
        return renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
            renderer.gameObject.scene==gameObject.scene &&
            !renderer.name.StartsWith("Eclipse floating fragment") &&
            renderer.bounds.size.sqrMagnitude<1000000f;
    }

    bool FindMonsterShot(MeshRenderer[] scenery)
    {
        cinematicMonster=null;
        float best=float.PositiveInfinity;
        foreach(var health in FindObjectsByType<Health>(FindObjectsSortMode.None))
        {
            if(monsterCloseups.Exists(shot=>shot.actor==health || MonsterModel(shot.actor)==MonsterModel(health))) continue;
            if(health.Team!=HealthTeam.Enemy || health.IsDead || !health.gameObject.activeInHierarchy ||
                health.gameObject.scene!=gameObject.scene) continue;
            var renderers=health.GetComponentsInChildren<Renderer>();
            Bounds bounds=default; bool visible=false;
            foreach(var renderer in renderers)
            {
                if(!renderer.enabled || renderer is ParticleSystemRenderer) continue;
                if(!visible) bounds=renderer.bounds; else bounds.Encapsulate(renderer.bounds);
                visible=true;
            }
            if(!visible || bounds.size.y<.4f) continue;
            Vector3 focus=bounds.center;
            float distance=Mathf.Clamp(bounds.size.y*4.5f,7,15);
            for(int side=0;side<8;side++)
            {
                Vector3 direction=Quaternion.AngleAxis(side*45,Vector3.up)*health.transform.forward;
                Vector3 start=focus+direction*distance+Vector3.up*bounds.size.y*.65f;
                Vector3 end=start+Vector3.Cross(Vector3.up,direction)*1.2f;
                if(ShotBlocked(start,focus,scenery) || ShotBlocked(end,focus,scenery)) continue;
                // Include skinned actors and scenery without colliders in the visibility check.
                bool blocked=false;
                foreach(var obstacle in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if(!obstacle.enabled || obstacle.transform.IsChildOf(health.transform) ||
                        obstacle is ParticleSystemRenderer || obstacle.bounds.size.x>50 || obstacle.bounds.size.z>50) continue;
                    var ray=new Ray(focus,(start-focus).normalized);
                    if(obstacle.bounds.IntersectRay(ray,out float enter) && enter<distance && enter>.15f)
                    { blocked=true; break; }
                }
                if(blocked) continue;
                float score=Vector3.Distance(world.player.position,health.transform.position)+side*.2f;
                if(score>=best) continue;
                best=score;
                cinematicMonster=health; monsterOrigin=health.transform.position; monsterFocus=focus;
                monsterShot=new Pose(start,Quaternion.LookRotation(focus-start));
                monsterEnd=new Pose(end,Quaternion.LookRotation(focus-end));
            }
        }
        return !float.IsPositiveInfinity(best);
    }

    string MonsterModel(Health actor)
    {
        if(!actor) return "";
        if(actor.GetComponent<StoneScoutRoadsideAI>()) return "StoneScout";
        if(actor.GetComponent<TrainingDummy>()) return "TrainingDummy";
        // Different placed instances of the same mesh are the same monster type.
        string key=actor.name.Replace("(Clone)",""); float largest=-1;
        foreach(var renderer in actor.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh=null;
            if(renderer is SkinnedMeshRenderer skin) mesh=skin.sharedMesh;
            else { var filter=renderer.GetComponent<MeshFilter>(); if(filter) mesh=filter.sharedMesh; }
            if(!mesh || renderer.bounds.size.sqrMagnitude<=largest) continue;
            largest=renderer.bounds.size.sqrMagnitude; key=mesh.name.Replace("(Clone)","");
        }
        return key;
    }

    bool ShotBlocked(Vector3 camera,Vector3 focus,MeshRenderer[] scenery)
    {
        Vector3 direction=camera-focus;
        float distance=direction.magnitude;
        if(distance<5) return false;
        var ray=new Ray(focus,direction/distance);
        foreach(var renderer in scenery)
        {
            if(!IsScenery(renderer)) continue;
            Bounds bounds=renderer.bounds;
            // Large terrain bounds enclose empty air; use its real collider below.
            if(bounds.size.x>50 || bounds.size.z>50) continue;
            bounds.Expand(2);
            // The tower itself belongs in frame rather than being treated as an obstruction.
            if(bounds.Contains(focus)) continue;
            if(bounds.Contains(camera) || (bounds.IntersectRay(ray,out float enter) && enter<distance+1))
                return true;
        }
        return Physics.SphereCast(focus+ray.direction*4,.75f,ray.direction,out _,
            distance-4,~0,QueryTriggerInteraction.Ignore);
    }

    bool TowerShotBlocked(Vector3 camera,Vector3 focus,MeshRenderer[] scenery)
    {
        Vector3 direction=(camera-focus).normalized;
        float distance=Vector3.Distance(camera,focus);
        // The pavilion's own columns and roof belong to the subject. Starting the
        // obstruction ray inside them rejected every arc and produced a static shot.
        const float subjectRadius=12;
        if(distance<=subjectRadius) return true;
        Ray ray=new Ray(focus+direction*subjectRadius,direction);
        foreach(var renderer in scenery)
        {
            if(!IsScenery(renderer)) continue;
            var bounds=renderer.bounds;
            if(bounds.size.x>50 || bounds.size.z>50 ||
                Vector3.Distance(bounds.ClosestPoint(focus),focus)<subjectRadius) continue;
            bounds.Expand(.5f);
            if(bounds.Contains(camera) || (bounds.IntersectRay(ray,out float enter) && enter<distance-subjectRadius)) return true;
        }
        return Physics.SphereCast(ray,.5f,distance-subjectRadius,~0,QueryTriggerInteraction.Ignore);
    }

    void LateUpdate()
    {
        if(!IsPlaying || !view) return;
        if(targetStage==2 && elapsed>=8.4f && elapsed<12.95f)
            foreach(var shot in monsterCloseups)
                if(shot.actor && !shot.actor.IsDead)
                {
                    var patrol=shot.actor.GetComponent<StoneScoutRoadsideAI>();
                    if(patrol) patrol.TickCinematicPatrol(Time.unscaledDeltaTime);
                }
        fade=0;
        Pose pose=skyShot;
        float t=Mathf.SmoothStep(0,1,(elapsed-.6f)/2.4f);
        // Establish the same moon immediately after the opening fade, so the entire
        // partial-to-total progression is visible before the monster montage begins.
        pose.rotation=targetStage==2 ? skyShot.rotation : Quaternion.Slerp(entry.rotation,skyShot.rotation,t);
        // Relocate only while black, so the camera never visibly travels through a tree.
        if(elapsed<1.3f)
        {
            fade=elapsed<.6f?Mathf.InverseLerp(0,.5f,elapsed):1-Mathf.InverseLerp(.7f,1.3f,elapsed);
            if(elapsed<.6f) pose=entry;
        }
        if(targetStage==2 && elapsed>=8.4f)
        {
            // Fade between distant shots so the camera never cuts through terrain.
            fade=elapsed<8.7f?Mathf.InverseLerp(8.4f,8.65f,elapsed):1-Mathf.InverseLerp(8.72f,8.95f,elapsed);
            if(elapsed>=8.7f)
            {
                float slot=Mathf.Clamp01((elapsed-8.7f)/4f)*Mathf.Max(1,monsterCloseups.Count);
                int index=Mathf.Min(monsterCloseups.Count-1,Mathf.FloorToInt(slot));
                float push=Mathf.SmoothStep(0,1,Mathf.Clamp01(slot-Mathf.Max(0,index)));
                // Never use the Tower as a fallback inside the monster montage.
                // Keep the last clear monster framing if an actor walks behind scenery.
                pose=hasLastMonsterPose?lastMonsterPose:skyShot;
                if(hasMonsterShot)
                {
                    // Stay on the scheduled actor. Falling back to an earlier actor
                    // here replayed the yellow monster during the second slot.
                    for(int candidate=0;candidate<1;candidate++)
                    {
                        var shot=monsterCloseups[(Mathf.Max(0,index)+candidate)%monsterCloseups.Count];
                        pose=shot.lastClear;
                        if(!shot.actor || shot.actor.IsDead || !shot.actor.gameObject.activeInHierarchy) continue;
                        Vector3 shift=shot.actor.transform.position-shot.origin;
                        Vector3 position=Vector3.Lerp(shot.start.position,shot.end.position,push);
                        if(ShotBlocked(position,shot.focus+shift,shotScenery)) continue;
                        pose=new Pose(position,Quaternion.LookRotation(shot.focus+shift-position));
                        shot.lastClear=pose;
                        lastMonsterPose=pose; hasLastMonsterPose=true;
                        break;
                    }
                }
            }
            if(elapsed>=12.7f)
            {
                fade=elapsed<12.95f?Mathf.InverseLerp(12.7f,12.93f,elapsed):1-Mathf.InverseLerp(13,13.25f,elapsed);
                if(elapsed>=12.95f) pose=towerShot;
            }
            if(elapsed>=13.25f)
            {
                float glide=Mathf.SmoothStep(0,1,(elapsed-13.25f)/7.45f);
                Vector3 position=towerOrbitFocus+Quaternion.AngleAxis(towerSweep*glide,Vector3.up)*towerOrbitOffset;
                pose=new Pose(position,Quaternion.LookRotation(flow.endingEntrance.position+Vector3.up*3-position));
            }
        }
        if(targetStage==1 && hasLampShot && elapsed>=5.3f)
        {
            fade=elapsed<5.65f?Mathf.InverseLerp(5.3f,5.6f,elapsed):1-Mathf.InverseLerp(5.7f,6,elapsed);
            if(elapsed>=5.65f)
            {
                float push=Mathf.SmoothStep(0,1,(elapsed-5.65f)/5.55f);
                pose=lampShot;
                pose.position+=lampShot.rotation*Vector3.forward*push*.5f;
            }
        }
        float restoreAt=Duration-1.3f;
        if(elapsed>=restoreAt)
        {
            fade=elapsed<restoreAt+.6f?Mathf.InverseLerp(restoreAt,restoreAt+.55f,elapsed):
                1-Mathf.InverseLerp(restoreAt+.7f,Duration,elapsed);
            if(elapsed>=restoreAt+.6f) pose=entry;
        }
        view.transform.SetPositionAndRotation(pose.position,pose.rotation);
        view.fieldOfView=elapsed>=restoreAt+.6f?entryFov:Mathf.Lerp(entryFov,48,t);
#if UNITY_EDITOR
        if(previewStage>0)
        {
            if(!capturedSky && elapsed>=5.3f) { capturedSky=true; CapturePreview("sky"); }
            if(!capturedTower && targetStage==2 && elapsed>=10) { capturedTower=true; CapturePreview("tower"); }
        }
#endif
    }

#if UNITY_EDITOR
    void CapturePreview(string shot)
    {
        string path=System.IO.Path.Combine(Application.temporaryCachePath,$"eclipse-{targetStage}-{shot}.png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("Eclipse preview capture: "+path);
    }
#endif

    void Finish()
    {
        int stage=targetStage;
        VisualStage=stage;
        Abort();
#if UNITY_EDITOR
        if(previewStage>=0)
        {
            Debug.Log("Eclipse preview finished; camera restored and gameplay release requested. Save unchanged.");
            return;
        }
#endif
        progress.eclipsePresentedStage=stage;
        flow.SaveNow();
        if(stage==2)
        {
            var map=GetComponent<BaselineMap>(); if(map) map.TrackTower(flow);
            world.ShowNotice("The Eclipse Warden awaits. Reach the Tower.");
        }
        else world.ShowNotice("The first seal has broken. The sky is changing.");
    }

    void Abort()
    {
        if(!IsPlaying) return;
        foreach(var shot in monsterCloseups)
            if(shot.actor)
            {
                var patrol=shot.actor.GetComponent<StoneScoutRoadsideAI>();
                if(patrol) patrol.EndCinematicPatrol();
            }
        IsPlaying=false; fade=0;
        if(view) { view.transform.SetPositionAndRotation(entry.position,entry.rotation); view.fieldOfView=entryFov; }
        if(follow) follow.SetCinematicControl(false);
        if(flow) flow.ReleaseTutorialCard();
    }

    void ApplyEnvironment(float stage)
    {
        if(stage<=.001f) { RestoreEnvironment(); return; }
        // Cool directional moonlight, hemispheric fill and distance haze provide depth.
        // No screen-wide black overlay: nearby surfaces and warm lamps stay readable.
        float amount=.46f*Mathf.Clamp01(stage)+.54f*Mathf.Clamp01(stage-1);
        Shader.SetGlobalFloat(WindowDarknessId,Mathf.Clamp01(amount));
        // One lighting update per frame: competing stage and cinematic values caused
        // repeated on/off transitions while the lantern shot was playing.
        if(lamps) lamps.SetEclipse(IsPlaying && targetStage==1
            ? Mathf.InverseLerp(5.85f,10.85f,elapsed) : stage,IsPlaying && targetStage==1);
        if(skyDirection.sqrMagnitude<.1f) skyDirection=new Vector3(.4f,.55f,1).normalized;
        if(sun)
        {
            sun.intensity=Mathf.Lerp(sunIntensity,.38f,amount);
            sun.color=Color.Lerp(sunColor,new Color(.62f,.67f,.78f),amount);
            sun.transform.rotation=Quaternion.Slerp(sunRotation,Quaternion.LookRotation(-skyDirection),amount);
        }
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientIntensity=ambientIntensity;
        RenderSettings.ambientSkyColor=Color.Lerp(ambientSky,new Color(.24f,.26f,.31f),amount);
        RenderSettings.ambientEquatorColor=Color.Lerp(ambientEquator,new Color(.17f,.18f,.21f),amount);
        RenderSettings.ambientGroundColor=Color.Lerp(ambientGround,new Color(.085f,.09f,.11f),amount);
        RenderSettings.reflectionIntensity=reflectionIntensity*Mathf.Lerp(1,.22f,amount);
        RenderSettings.fog=true;
        RenderSettings.fogMode=FogMode.ExponentialSquared;
        RenderSettings.fogDensity=Mathf.Lerp(originalFog?fogDensity:0,.010f,amount);
        RenderSettings.fogColor=Color.Lerp(fogColor,new Color(.13f,.12f,.15f),amount);
        if(eclipseSky && stage>.001f)
        {
            if(skyDirection.sqrMagnitude<.1f) skyDirection=new Vector3(.4f,.55f,1).normalized;
            eclipseSky.SetFloat("_Stage",stage); eclipseSky.SetFloat("_Clock",clock);
            // Give the lunar shadow its own slower clock without delaying sky or lamps.
            float shadowStage=IsPlaying
                ? Mathf.Lerp(targetStage==2?1:fromStage,targetStage,
                    Mathf.SmoothStep(0,1,targetStage==2?Mathf.InverseLerp(1.3f,8.1f,elapsed):elapsed/8f)) : stage;
            eclipseSky.SetFloat("_OcclusionStage",shadowStage);
            eclipseSky.SetVector("_Direction",skyDirection); RenderSettings.skybox=eclipseSky;
        }
        else RenderSettings.skybox=originalSky;
    }

    void OnGUI()
    {
        if(!IsPlaying) return;
        var oldColor=GUI.color; int depth=GUI.depth; var matrix=GUI.matrix;
        GUI.matrix=Matrix4x4.identity; GUI.depth=-120;
        float bar=Screen.height*.09f;
        GUI.color=new Color(0,0,0,Mathf.Clamp01(elapsed*2));
        GUI.DrawTexture(new Rect(0,0,Screen.width,bar),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0,Screen.height-bar,Screen.width,bar),Texture2D.whiteTexture);
        if(subtitle==null)
        {
            subtitle=new GUIStyle(GUI.skin.label) { alignment=TextAnchor.MiddleCenter,wordWrap=true };
            subtitle.normal.textColor=new Color(1,.92f,.78f);
            caption=new GUIStyle(subtitle);
        }
        subtitle.fontSize=Mathf.RoundToInt(Mathf.Clamp(Screen.height/36f,16,32));
        caption.fontSize=Mathf.RoundToInt(Mathf.Clamp(Screen.height/55f,12,20));
        GUI.color=Color.white;
        string line=targetStage==1?"One seal is broken. The world begins to fracture.":elapsed<7?
            "Both seals are broken. The sun falls into shadow.":
            "ECLIPSE WARDEN\nYou have proven your strength. Come to the Tower and challenge me.";
        GUI.Label(new Rect(Screen.width*.12f,Screen.height-bar-80,Screen.width*.76f,76),line,subtitle);
        GUI.Label(new Rect(0,Screen.height-bar,Screen.width,bar),"Esc  ·  Skip",caption);
        if(fade>0) { GUI.color=new Color(0,0,0,fade); GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture); }
        GUI.color=oldColor; GUI.depth=depth; GUI.matrix=matrix;
    }

    void RestoreEnvironment()
    {
        Shader.SetGlobalFloat(WindowDarknessId,0);
        if(lamps) lamps.SetEclipse(0);
        if(!captured) return;
        if(sun) { sun.intensity=sunIntensity; sun.color=sunColor; sun.transform.rotation=sunRotation; }
        if(RenderSettings.skybox==eclipseSky) RenderSettings.skybox=originalSky;
        RenderSettings.ambientSkyColor=ambientSky; RenderSettings.ambientEquatorColor=ambientEquator;
        RenderSettings.ambientGroundColor=ambientGround; RenderSettings.ambientIntensity=ambientIntensity;
        RenderSettings.reflectionIntensity=reflectionIntensity; RenderSettings.fogColor=fogColor; RenderSettings.fogDensity=fogDensity;
        RenderSettings.fog=originalFog; RenderSettings.fogMode=originalFogMode;
        RenderSettings.ambientMode=originalAmbientMode;
    }
    void OnDisable() { Abort(); RestoreEnvironment(); }
    void OnDestroy()
    {
        RestoreEnvironment();
        if(eclipseSky) Destroy(eclipseSky);
    }
}
