using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Click picking uses renderer bounds, so the companion adds no combat or movement collider.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public sealed class PaimonOrbitInteraction : MonoBehaviour
{
    public Transform center;
    public PaimonMotion motion;
    public bool externallyDriven;
    public bool inputEnabled=true;
    [Min(1)] public float duration=5.6f;
    public Func<bool> CanInteract;
    public bool IsOrbiting { get; private set; }
    public float Progress { get; private set; }
    public int CompletedLaps { get; private set; }
    public static bool ConsumedClickThisFrame => consumedFrame==Time.frameCount;
    static int consumedFrame=-1;
    Vector3 offset, previousCenter;
    Quaternion initialRotation;
    float elapsed, phase;
    Renderer[] surfaces;
    public float FlightBlend => IsOrbiting?Mathf.Sin(Mathf.PI*Progress):0;
    static float Ease(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
    void Awake(){surfaces=GetComponentsInChildren<Renderer>(true);}
    public bool TryBegin()
    {
        if(!isActiveAndEnabled||!center||!motion||!motion.visual||!motion.visual.gameObject.activeInHierarchy||IsOrbiting||(CanInteract!=null&&!CanInteract()))return false;
        offset=transform.position-center.position;
        if(new Vector2(offset.x,offset.z).sqrMagnitude<.09f)return false;
        initialRotation=transform.rotation;previousCenter=center.position;
        elapsed=0;Progress=0;IsOrbiting=true;return true;
    }
    public void Cancel(){IsOrbiting=false;Progress=0;elapsed=0;}
    void OnDisable(){Cancel();}
    public bool TryClick(Ray ray)
    {
        if(!inputEnabled||!center||(CanInteract!=null&&!CanInteract()))return false;
        if(surfaces==null||surfaces.Length==0)surfaces=GetComponentsInChildren<Renderer>(true);
        float distance=float.PositiveInfinity;
        foreach(var r in surfaces)if(r&&r.enabled&&r.gameObject.activeInHierarchy)
        {var bounds=r.bounds;bounds.Expand(.08f);if(bounds.IntersectRay(ray,out float d))distance=Mathf.Min(distance,d);}
        if(float.IsPositiveInfinity(distance))return false;
        foreach(var hit in Physics.RaycastAll(ray,distance,~0,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform))return false;
        if(!IsOrbiting&&!TryBegin())return false;
        consumedFrame=Time.frameCount;return true;
    }
    void Update()
    {
        if(Time.timeScale<=0||Mouse.current==null||!Mouse.current.leftButton.wasPressedThisFrame||!Camera.main)return;
        if(EventSystem.current&&EventSystem.current.IsPointerOverGameObject())return;
        Vector2 point=Cursor.lockState==CursorLockMode.Locked?new Vector2(Screen.width*.5f,Screen.height*.5f):Mouse.current.position.ReadValue();
        TryClick(Camera.main.ScreenPointToRay(point));
    }
    public bool Advance(float dt,float animationTime)
    {
        if(!IsOrbiting)return false;
        if(!center||!motion||(CanInteract!=null&&!CanInteract())||Vector3.Distance(center.position,previousCenter)>8){Cancel();return false;}
        previousCenter=center.position;
        elapsed=Mathf.Min(Mathf.Max(1,duration),elapsed+Mathf.Max(0,dt));
        Progress=elapsed/Mathf.Max(1,duration);
        float arc=Progress<.42f?Ease(Progress/.42f):Progress>.58f?1-Ease((Progress-.58f)/.42f):1;
        float angle=arc*Mathf.PI;
        Vector3 radial=Quaternion.AngleAxis(angle*Mathf.Rad2Deg,Vector3.up)*new Vector3(offset.x,0,offset.z);
        Vector3 desired=center.position+radial+Vector3.up*(offset.y+.10f*Mathf.Sin(Mathf.PI*Progress)*Mathf.Sin(Mathf.PI*Progress));
        // Preserve angular progress when an obstacle requires a smaller radius.
        Vector3 origin=center.position+Vector3.up*offset.y;
        foreach(var hit in Physics.RaycastAll(origin,radial.normalized,radial.magnitude+.15f,~0,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(center)&&!hit.transform.IsChildOf(transform)&&hit.distance<Vector3.Distance(origin,desired)+.15f)
                desired=origin+radial.normalized*Mathf.Max(.25f,hit.distance-.15f)+Vector3.up*(desired.y-origin.y);
        transform.position=desired;
        float blend=Ease(Mathf.Min(Progress,1-Progress)/.16f);
        Vector3 outbound=Vector3.Cross(Vector3.up,radial).normalized;
        float turn=Ease((Progress-.40f)/.20f);
        Quaternion heading=Quaternion.AngleAxis(turn*180,Vector3.up)*Quaternion.LookRotation(outbound);
        transform.rotation=Quaternion.Slerp(initialRotation,heading,blend);
        motion.SampleOrbit(animationTime,Progress);
        if(Progress>=1){IsOrbiting=false;CompletedLaps++;}
        return true;
    }
    void LateUpdate()
    {
        if(externallyDriven||!motion)return;
        phase+=Time.deltaTime;
        if(!Advance(Time.deltaTime,phase))motion.Sample(phase,PaimonMotion.Action.Float,0);
    }
}
