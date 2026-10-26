using UnityEngine;
using UnityEngine.Rendering;

// Adapts the existing Scout gameplay contract to the authored DemonKing skeleton.
// Blade contact is sampled from the same transformed mesh points as its red wake.
[DefaultExecutionOrder(80)]
public sealed class DemonKingCombatVisual : StoneScoutVisualMotion
{
    public Animator pose;
    public Transform blade;
    public Vector3 bladeTipLocal;
    public Vector3 bladeBaseLocal;
    public Transform chest;
    public SkinnedMeshRenderer bodySkin;
    public Renderer weaponRenderer;
    public Material energyMaterial;
    public Material hitMaterial;
    public float attackDuration=1.5f;
    public float contactStart=.66f;
    public float contactEnd=.86f;
    public float hitDuration=.42f;
    enum Action { None, Attack, Hit, Dead }
    Action action;
    float elapsed, charge, flash, trailAge;
    bool moving, sampled;
    Vector3 lastBase,lastTip;
    Health health;
    PlayerVitals player;
    float playerSearch, resumedPoseSpeed=1f;
    bool posePaused;
    Mesh trailMesh;
    MeshRenderer trailRenderer;
    SkinnedMeshRenderer hitOverlay;
    MaterialPropertyBlock weaponBlock,trailBlock,hitBlock;
    readonly Vector3[] historyA=new Vector3[12],historyB=new Vector3[12];
    readonly float[] historyTime=new float[12];
    readonly Vector3[] vertices=new Vector3[24];
    readonly Color[] colors=new Color[24];
    readonly Vector2[] uvs=new Vector2[24];
    readonly int[] triangles=new int[66];
    readonly RaycastHit[] castHits=new RaycastHit[48];
    int count;
    public override bool IsDead=>action==Action.Dead;
    public override Vector3 TorsoWorldPosition=>chest?chest.position:transform.position+Vector3.up;
    public override Vector3 RightFistWorldPosition=>BladeTip;
    public Vector3 BladeTip=>blade?blade.TransformPoint(bladeTipLocal):transform.position;
    public Vector3 BladeBase=>blade?blade.TransformPoint(bladeBaseLocal):transform.position;
    public SkinnedMeshRenderer BodyRenderer=>bodySkin;
    public float AttackElapsed=>action==Action.Attack?elapsed:-1;
    public bool ContactOpen=>action==Action.Attack && elapsed>=contactStart && elapsed<=contactEnd;
    public int SwingCount {get;private set;}
    public int ContactCount {get;private set;}
    public bool TrailVisible=>trailRenderer && trailRenderer.enabled;
    public bool WarningVisible=>charge>.05f && !IsDead;

    protected override void Awake()
    {
        health=GetComponentInParent<Health>();weaponBlock=new MaterialPropertyBlock();trailBlock=new MaterialPropertyBlock();hitBlock=new MaterialPropertyBlock();
        if(health)AdventureCreatureVoice.Ensure(health.gameObject);
        FindPlayer();
        var go=new GameObject("Short actual blade wake",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
        trailMesh=new Mesh{name="Bounded DemonKing blade wake"};trailMesh.MarkDynamic();go.GetComponent<MeshFilter>().sharedMesh=trailMesh;
        trailRenderer=go.GetComponent<MeshRenderer>();trailRenderer.sharedMaterial=energyMaterial;trailRenderer.shadowCastingMode=ShadowCastingMode.Off;trailRenderer.receiveShadows=false;trailRenderer.enabled=false;
        for(int i=0;i<11;i++){int t=i*6,v=i*2;triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v+1;triangles[t+4]=v+3;triangles[t+5]=v+2;}
        if(bodySkin && hitMaterial)
        {
            var patch=new GameObject("Skinned local hit flash");patch.transform.SetParent(bodySkin.transform,false);hitOverlay=patch.AddComponent<SkinnedMeshRenderer>();
            hitOverlay.sharedMesh=bodySkin.sharedMesh;hitOverlay.bones=bodySkin.bones;hitOverlay.rootBone=bodySkin.rootBone;hitOverlay.localBounds=bodySkin.localBounds;
            var materials=new Material[bodySkin.sharedMesh.subMeshCount];for(int i=0;i<materials.Length;i++)materials[i]=hitMaterial;
            hitOverlay.sharedMaterials=materials;hitOverlay.shadowCastingMode=ShadowCastingMode.Off;hitOverlay.receiveShadows=false;hitOverlay.enabled=false;
        }
    }
    void OnEnable(){if(health)health.Damaged+=OnDamage;}
    void OnDisable(){if(health)health.Damaged-=OnDamage;ClearCues();}
    void OnDamage(Health target,DamageInfo damage)
    {
        if(target.IsDead)return;flash=.18f;
        hitBlock.SetVector("_ImpactPoint",damage.HitPoint);hitBlock.SetFloat("_ImpactRadius",.65f);
        hitBlock.SetColor("_ImpactTint",new Color(1,.4f,.12f));
    }
    protected override void Update()
    {
        if(!player && Time.unscaledTime>=playerSearch)FindPlayer();
        bool active=Time.timeScale>0 && (!player || (player.gameObject.scene==gameObject.scene && player.EnemyCombatActive));
        if(!active)
        {
            if(pose && !posePaused){resumedPoseSpeed=pose.speed;pose.speed=0;posePaused=true;}
            sampled=false;count=0;if(trailRenderer)trailRenderer.enabled=false;if(hitOverlay)hitOverlay.enabled=false;
            return;
        }
        if(posePaused){if(pose)pose.speed=resumedPoseSpeed;posePaused=false;}
        elapsed+=Time.deltaTime;flash=Mathf.Max(0,flash-Time.deltaTime);
        if(action==Action.Attack && elapsed>=attackDuration || action==Action.Hit && elapsed>=hitDuration)
        {action=Action.None;PlayLocomotion();}
    }
    void FindPlayer()
    {
        playerSearch=Time.unscaledTime+1f;
        foreach(var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
            if(candidate.gameObject.scene==gameObject.scene){player=candidate;break;}
    }
    public override void SetMoving(bool value)
    {if(moving==value)return;moving=value;if(action==Action.None)PlayLocomotion();}
    void PlayLocomotion(){if(pose && pose.isActiveAndEnabled)pose.CrossFadeInFixedTime(moving?"Walk":"Idle",.12f);}
    public override void PlayAttack()
    {if(IsDead)return;action=Action.Attack;elapsed=charge=0;count=0;sampled=false;SwingCount++;pose.Play("Attack",0,0);}
    public override void CancelAttack()
    {if(action!=Action.Attack)return;action=Action.None;elapsed=0;ClearCues();PlayLocomotion();}
    public override void PlayHit(Vector3 direction)
    {if(IsDead)return;action=Action.Hit;elapsed=0;ClearCues();pose.Play("Hit",0,0);}
    public override void PlayDeath()
    {action=Action.Dead;moving=false;elapsed=0;ClearCues();pose.Play("Death",0,0);}
    public override void ResetVisual()
    {action=Action.None;moving=false;elapsed=flash=0;ClearCues();if(pose)pose.Play("Idle",0,0);}
    public override void SetChargeProgress(float value)
    {charge=IsDead?0:Mathf.Clamp01(value);if(charge<=0)ApplyBladeCharge();}
    void ApplyBladeCharge()
    {
        if(!weaponRenderer || weaponBlock==null)return;
        weaponRenderer.GetPropertyBlock(weaponBlock);
        weaponBlock.SetColor("_EmissionColor",new Color(.85f,.012f,.004f)*charge);
        weaponBlock.SetFloat("_Charge",charge);weaponRenderer.SetPropertyBlock(weaponBlock);
    }
    void ClearCues(){charge=0;count=0;sampled=false;ApplyBladeCharge();if(trailRenderer)trailRenderer.enabled=false;if(hitOverlay)hitOverlay.enabled=false;}

    // Swept samples along the actual cutting edge prevent a fast blade tunnelling
    // through a close capsule. Cover is tested along each accepted contact path.
    public bool TryBladeContact(CharacterController target,out Vector3 point)
    {
        point=default;if(!ContactOpen || !target || !blade)return false;
        Vector3 a=BladeBase,b=BladeTip;const float radius=.16f;
        for(int i=0;i<=8;i++)
        {
            float t=i/8f;Vector3 current=Vector3.Lerp(a,b,t),closest=target.ClosestPoint(current);
            if((closest-current).sqrMagnitude<=radius*radius && ClearPath(current,closest,target))
            {point=closest;ContactCount++;return true;}
            if(!sampled)continue;
            Vector3 previous=Vector3.Lerp(lastBase,lastTip,t),delta=current-previous;
            if(delta.sqrMagnitude<.000001f)continue;
            int n=Physics.SphereCastNonAlloc(previous,radius,delta.normalized,castHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(n==castHits.Length)continue;
            float distance=float.PositiveInfinity;Collider first=null;Vector3 hitPoint=default;
            for(int j=0;j<n;j++)if(castHits[j].collider && !castHits[j].collider.transform.IsChildOf(health.transform) && castHits[j].distance<distance)
            {distance=castHits[j].distance;first=castHits[j].collider;hitPoint=castHits[j].point;}
            if(first && first.transform.IsChildOf(target.transform) && ClearPath(previous,hitPoint,target))
            {point=hitPoint;ContactCount++;return true;}
        }
        return false;
    }
    bool ClearPath(Vector3 from,Vector3 to,CharacterController target)
    {
        Vector3 delta=to-from;if(delta.sqrMagnitude<.000001f)return true;
        int n=Physics.RaycastNonAlloc(from,delta.normalized,castHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        if(n==castHits.Length)return false;
        for(int i=0;i<n;i++)if(castHits[i].collider && !castHits[i].collider.transform.IsChildOf(health.transform) && !castHits[i].collider.transform.IsChildOf(target.transform))return false;
        return true;
    }
    protected override void LateUpdate()
    {
        if(!blade || Time.deltaTime<=0 || (player && !player.EnemyCombatActive)){sampled=false;return;}
        bool live=health && !health.IsDead && action!=Action.Dead;
        ApplyBladeCharge();
        if(hitOverlay){hitOverlay.enabled=live && flash>0;hitBlock.SetFloat("_ImpactStrength",flash/.18f);hitOverlay.SetPropertyBlock(hitBlock);}
        bool stroke=live && action==Action.Attack && elapsed>=contactStart-.035f && elapsed<=contactEnd;
        if(stroke)
        {
            for(int i=Mathf.Min(count,11);i>0;i--){historyA[i]=historyA[i-1];historyB[i]=historyB[i-1];historyTime[i]=historyTime[i-1];}
            historyA[0]=BladeBase;historyB[0]=BladeTip;historyTime[0]=Time.time;count=Mathf.Min(count+1,12);
        }
        bool visible=live && count>=2 && Time.time-historyTime[0]<.16f && EnemyPresentationBudget.Details(transform.position);
        trailRenderer.enabled=visible;
        if(visible)
        {
            for(int i=0;i<12;i++)
            {
                int k=Mathf.Min(i,count-1);float fade=Mathf.Clamp01(1-(Time.time-historyTime[k])/.16f);
                vertices[i*2]=trailRenderer.transform.InverseTransformPoint(historyA[k]);vertices[i*2+1]=trailRenderer.transform.InverseTransformPoint(historyB[k]);
                colors[i*2]=new Color(1,.12f,.035f,fade*.20f);colors[i*2+1]=new Color(1,.32f,.08f,fade*.8f);
                uvs[i*2]=new Vector2(i/11f,0);uvs[i*2+1]=new Vector2(i/11f,1);
            }
            trailMesh.Clear();trailMesh.vertices=vertices;trailMesh.colors=colors;trailMesh.uv=uvs;trailMesh.triangles=triangles;trailMesh.RecalculateBounds();
            trailBlock.SetColor("_Tint",new Color(1,.12f,.045f));trailBlock.SetFloat("_Clock",Time.time);trailBlock.SetFloat("_Fade",.85f);trailRenderer.SetPropertyBlock(trailBlock);
        }
        lastBase=BladeBase;lastTip=BladeTip;sampled=live;
    }
    void OnDestroy(){if(trailMesh)Destroy(trailMesh);}
}
