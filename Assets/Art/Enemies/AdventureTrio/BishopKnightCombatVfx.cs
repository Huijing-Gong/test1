using UnityEngine;
using UnityEngine.Rendering;

// Reusable, world-space presentation. No colliders, damage, physics or animation
// events live here. All timings and footprints are supplied by the actual brain.
[DisallowMultipleComponent, DefaultExecutionOrder(220)]
public sealed class BishopKnightCombatVfx : MonoBehaviour
{
    const int Segments = 32;
    static readonly Color Gold = new Color(1f, .68f, .16f);
    static readonly Color Mint = new Color(.26f, .91f, .82f);
    BishopKnightAI knight;
    GameObject holder;
    Material material;
    Mesh footprint, shield;
    MeshRenderer floorRenderer, shieldRenderer;
    LineRenderer border, guardEdge, progress;
    LineRenderer arena, opening, rallyWave;
    readonly Vector3[] arenaPoints = new Vector3[97];
    TrailRenderer trail;
    readonly Vector3[] floorVertices = new Vector3[Segments + 2];
    readonly Vector3[] shieldVertices = new Vector3[(Segments + 1) * 2];
    readonly Color[] floorColors = new Color[Segments + 2];
    readonly Color[] shieldColors = new Color[(Segments + 1) * 2];
    readonly RaycastHit[] groundHits = new RaycastHit[24];
    float clock, flash;
    bool wasActive;
    int lastSwings = -1;
    int floorSwing = -1;
    CapsuleCollider shape;
    public bool GroundVisible => floorRenderer && floorRenderer.enabled;
    public bool GuardVisible => shieldRenderer && shieldRenderer.enabled;
    public bool TrailVisible => trail && trail.emitting;

    void Start()
    {
        knight = GetComponent<BishopKnightAI>();
        shape = GetComponent<CapsuleCollider>();
        var shader = Resources.Load<Shader>("KnightCombatCue");
        if (!knight || !shader) { enabled = false; return; }
        material = new Material(shader) { name = "Sentinel combat cue", hideFlags = HideFlags.DontSave };
        holder = new GameObject("Sentinel reusable combat cues");
        holder.hideFlags = HideFlags.DontSave;
        // Deliberately no parent: vertices are world space, independent of model scale.
        footprint = MakeMesh("Attack footprint", out floorRenderer);
        shield = MakeMesh("Frontal shield energy", out shieldRenderer);
        footprint.vertices = floorVertices;
        var fan = new int[Segments * 3];
        for (int i = 0; i < Segments; i++) { fan[i*3] = 0; fan[i*3+1] = i+1; fan[i*3+2] = i+2; }
        footprint.triangles = fan;
        shield.vertices = shieldVertices;
        var strip = new int[Segments * 6];
        for (int i = 0; i < Segments; i++)
        { int n=i*6,a=i*2; strip[n]=a; strip[n+1]=a+1; strip[n+2]=a+2;
          strip[n+3]=a+1; strip[n+4]=a+3; strip[n+5]=a+2; }
        shield.triangles = strip;
        border = Line("Attack perimeter", .04f);
        guardEdge = Line("Shield upper rim", .035f);
        progress = Line("Attack commitment", .042f);
        arena=Line("Sentinel territory inlay",.025f);
        arena.positionCount=arenaPoints.Length;
        for(int i=0;i<arenaPoints.Length;i++)
        {
            float a=i/(float)(arenaPoints.Length-1)*Mathf.PI*2;
            arenaPoints[i]=Ground(knight.Home+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*5f);
        }
        arena.SetPositions(arenaPoints);
        opening=Line("Shield down opening",.045f);
        rallyWave=Line("Phase two resonance",.05f);
        var tip = new GameObject("Native spear tip trail"); tip.transform.SetParent(holder.transform);
        trail = tip.AddComponent<TrailRenderer>();
        trail.sharedMaterial = material; trail.time = .22f; trail.minVertexDistance = .015f;
        trail.widthCurve = AnimationCurve.Linear(0, .22f, 1, 0);
        trail.startColor = new Color(1,.93f,.65f,.95f); trail.endColor = new Color(1,.55f,.08f,0);
        trail.shadowCastingMode = ShadowCastingMode.Off; trail.receiveShadows = false;
        trail.emitting = false; Hide();
    }

    Mesh MakeMesh(string label, out MeshRenderer renderer)
    {
        var go = new GameObject(label); go.transform.SetParent(holder.transform, false);
        var mesh = new Mesh { name = label }; mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return mesh;
    }
    LineRenderer Line(string label, float width)
    {
        var go = new GameObject(label); go.transform.SetParent(holder.transform, false);
        var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
        line.useWorldSpace = true; line.widthMultiplier = width; line.numCornerVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        return line;
    }
    public void ShieldContact(Vector3 point) { flash = .18f; }

    void LateUpdate()
    {
        if (!holder || !knight) return;
        bool active = knight.PresentationActive;
        if (!active) { Hide(); wasActive = false; return; }
        if (!wasActive || lastSwings != knight.Swings) trail.Clear();
        wasActive = true; lastSwings = knight.Swings;
        clock += Time.deltaTime; flash = Mathf.Max(0, flash-Time.deltaTime);
        DrawFloor(); DrawGuard(); DrawArena();
        trail.transform.position = knight.SpearTip;
        trail.enabled = true;
        trail.emitting = knight.Attacking && knight.PhaseElapsed <= knight.StrikeCloseTime;
    }

    Vector3 Ground(Vector3 p)
    {
        int count = Physics.RaycastNonAlloc(p+Vector3.up*1.4f,Vector3.down,groundHits,2.8f,~0,QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity; float height = p.y;
        for(int i=0;i<count;i++)
        {
            var hit=groundHits[i];
            if(!hit.collider || hit.normal.y < .7f || hit.collider.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<PlayerVitals>() || hit.collider.GetComponentInParent<AdventureCreatureAI>()) continue;
            if(hit.distance<nearest) { nearest=hit.distance; height=hit.point.y; }
        }
        return new Vector3(p.x,height+.045f,p.z);
    }

    void DrawFloor()
    {
        bool visible = knight.Telegraphing || (knight.Attacking && knight.PhaseElapsed <= knight.StrikeCloseTime);
        floorRenderer.enabled = border.enabled = progress.enabled = visible;
        if(!visible) return;
        float charge = Mathf.Clamp01(knight.PhaseElapsed / Mathf.Max(.01f,knight.StrikeOpenTime));
        Color tint = Gold;
        float opacity = knight.Telegraphing ? .18f+.14f*charge : .14f;
        float range=knight.AttackReach;
        Vector3 origin=knight.AttackOrigin;
        Quaternion facing=knight.AttackFacing;
        // Locked footprint: project onto terrain once per swing, not 35 rays/frame.
        if (floorSwing != knight.Swings || (knight.Telegraphing && !knight.AimLocked))
        {
        floorSwing = knight.Swings;
        floorVertices[0]=Ground(origin);
        // Rectangular thrust: perimeter subdivided into the same reusable fan.
        for(int i=0;i<=Segments;i++)
        {
            Vector3 local;
            if(knight.IsThrust)
            {
                float t=i/(float)Segments*4; int edge=Mathf.Min(3,(int)t); float f=t-edge;
                float w=knight.ThrustHalfWidth;
                Vector3 a,b;
                if(edge==0){a=new Vector3(-w,0,0);b=new Vector3(-w,0,range);}
                else if(edge==1){a=new Vector3(-w,0,range);b=new Vector3(w,0,range);}
                else if(edge==2){a=new Vector3(w,0,range);b=new Vector3(w,0,0);}
                else {a=new Vector3(w,0,0);b=new Vector3(-w,0,0);}
                local=Vector3.Lerp(a,b,f);
            }
            else
            {
                float angle=Mathf.Lerp(-knight.SweepHalfAngle,knight.SweepHalfAngle,i/(float)Segments)*Mathf.Deg2Rad;
                local=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*range;
            }
            floorVertices[i+1]=Ground(origin+facing*local);
        }
        footprint.vertices=floorVertices; footprint.RecalculateBounds();
        }
        for(int i=0;i<floorColors.Length;i++) floorColors[i]=new Color(tint.r,tint.g,tint.b,opacity);
        footprint.colors=floorColors;
        border.positionCount=knight.IsThrust?Segments+1:Segments+3;
        if(knight.IsThrust) for(int i=0;i<=Segments;i++) border.SetPosition(i,floorVertices[i+1]);
        else {border.SetPosition(0,floorVertices[0]);for(int i=0;i<=Segments;i++)border.SetPosition(i+1,floorVertices[i+1]);border.SetPosition(Segments+2,floorVertices[0]);}
        border.startColor=border.endColor=new Color(tint.r,tint.g,tint.b,.5f+.4f*charge);
        border.widthMultiplier=knight.AimLocked?.055f:.03f;
        // A moving narrow front conveys charge without inflating the hit range.
        progress.positionCount=Segments+1;
        for(int i=0;i<=Segments;i++)progress.SetPosition(i,Vector3.Lerp(floorVertices[0],floorVertices[i+1],charge));
        progress.startColor=progress.endColor=new Color(1,.94f,.65f,knight.Telegraphing?.55f:0);
    }

    void DrawGuard()
    {
        bool visible=knight.ShieldRaised;
        shieldRenderer.enabled=guardEdge.enabled=visible;
        if(!visible)return;
        var bounds=shape.bounds;
        float height=bounds.size.y, radius=Mathf.Max(Mathf.Max(bounds.extents.x,bounds.extents.z)+.24f,height*.50f);
        Vector3 root=new Vector3(transform.position.x,bounds.min.y+.12f,transform.position.z);
        float alpha=.14f+.025f*Mathf.Sin(clock*2.8f)+flash*.8f;
        guardEdge.positionCount=Segments+1;
        for(int i=0;i<=Segments;i++)
        {
            float angle=Mathf.Lerp(-knight.ShieldHalfAngle,knight.ShieldHalfAngle,i/(float)Segments)*Mathf.Deg2Rad;
            Vector3 p=root+transform.rotation*new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*radius;
            shieldVertices[i*2]=p; shieldVertices[i*2+1]=p+Vector3.up*(height*.85f);
            float edgeFade=Mathf.Sin(i/(float)Segments*Mathf.PI);
            shieldColors[i*2]=new Color(Mint.r,Mint.g,Mint.b,alpha*.25f*edgeFade);
            shieldColors[i*2+1]=new Color(Mint.r,Mint.g,Mint.b,alpha*edgeFade);
            guardEdge.SetPosition(i,shieldVertices[i*2+1]);
        }
        shield.vertices=shieldVertices;shield.colors=shieldColors;shield.RecalculateBounds();
        guardEdge.startColor=guardEdge.endColor=new Color(Mint.r,Mint.g,Mint.b,.28f+flash*2);
    }
    void DrawArena()
    {
        arena.enabled=knight.TerritoryEngaged;
        arena.startColor=arena.endColor=knight.Enraged?new Color(1,.75f,.3f,.3f):new Color(.72f,.77f,.67f,.18f);
        opening.enabled=knight.Recovering || knight.GuardBroken;
        if(opening.enabled)
        {
            opening.positionCount=25;
            for(int i=0;i<25;i++)
            {
                float a=i/24f*Mathf.PI*2;
                opening.SetPosition(i,transform.position+new Vector3(Mathf.Sin(a)*.95f,.08f,Mathf.Cos(a)*.95f));
            }
            opening.startColor=opening.endColor=new Color(.4f,1,.8f,.4f);
        }
        rallyWave.enabled=knight.Rallying;
        if(rallyWave.enabled)
        {
            float t=Mathf.Clamp01(knight.PhaseElapsed/.95f);
            rallyWave.positionCount=49;
            for(int i=0;i<49;i++)
            {
                float a=i/48f*Mathf.PI*2;
                rallyWave.SetPosition(i,Ground(transform.position+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*Mathf.Lerp(.8f,2.7f,t)));
            }
            rallyWave.startColor=rallyWave.endColor=new Color(1,.83f,.4f,(1-t)*.75f);
        }
    }
    void Hide()
    {
        if(floorRenderer)floorRenderer.enabled=false;if(shieldRenderer)shieldRenderer.enabled=false;
        if(border)border.enabled=false;if(guardEdge)guardEdge.enabled=false;if(progress)progress.enabled=false;
        if(arena)arena.enabled=false;if(opening)opening.enabled=false;if(rallyWave)rallyWave.enabled=false;
        if(trail){trail.emitting=false;trail.Clear();trail.enabled=false;}
    }
    void OnDisable(){Hide();wasActive=false;}
    void OnDestroy()
    {
        if(holder)Destroy(holder);if(footprint)Destroy(footprint);if(shield)Destroy(shield);if(material)Destroy(material);
    }
}
