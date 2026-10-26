using UnityEngine;

// Only the authoritative KO handler calls this, before repositioning fighters.
public sealed class BattleKOPresentation:MonoBehaviour
{
    BattleJPresentation.InkMesh mesh;Material material;float age;Vector3 position;Vector2 screenAnchor;
    public static void Play(Vector3 point,bool won)
    {
        var camera=Camera.main;if(!camera)return;
        var viewport=camera.WorldToViewportPoint(point);
        viewport.x=Mathf.Clamp(viewport.x,.06f,.94f);viewport.y=Mathf.Clamp(viewport.y,.08f,.92f);
        viewport.z=Mathf.Max(1,Vector3.Dot(point-camera.transform.position,camera.transform.forward));
        var go=new GameObject("Confirmed KO edge star");var cue=go.AddComponent<BattleKOPresentation>();
        cue.screenAnchor=new Vector2(viewport.x,viewport.y);
        cue.position=camera.ViewportToWorldPoint(viewport);cue.position.z=-.7f;
        cue.material=new Material(Resources.Load<Shader>("VFX/BattleJInk")){hideFlags=HideFlags.DontSave};
        cue.mesh=new BattleJPresentation.InkMesh("KO star",go.transform,cue.material);
        BattleCameraController.RequestPresentationShake(point,4.5f,.14f,Vector2.one);
    }
    void LateUpdate()
    {
        if(Time.timeScale<=0)return;
        age+=Mathf.Min(Time.unscaledDeltaTime,.033f);
        if(age>=.36f){Destroy(gameObject);return;}
        float t=age/.36f,r=.3f+Mathf.Sqrt(t)*.9f;
        var camera=Camera.main;
        if(camera)
        {
            // Remain screen-sized while the normal battle camera reframes after KO.
            var ray=camera.ViewportPointToRay(new Vector3(screenAnchor.x,screenAnchor.y,0));
            var plane=new Plane(Vector3.forward,new Vector3(0,0,-.7f));
            if(plane.Raycast(ray,out float distance))position=ray.GetPoint(distance);
            float depth=Mathf.Max(1,Vector3.Dot(position-camera.transform.position,camera.transform.forward));
            float height=camera.orthographic?camera.orthographicSize*2:2*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
            r=height/720f*(18+Mathf.Sqrt(t)*36);
        }
        mesh.Begin(position);
        Color gold=new Color( .92f,.75f,.33f,(1-t)*(1-t));
        Color cream=new Color(.97f,.93f,.80f,gold.a);
        Color mint=new Color(.48f,.84f,.75f,gold.a);
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;
            Vector2 p=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(i%2==0?r:r*.2f);
            Vector2 q=new Vector2(Mathf.Cos(b),Mathf.Sin(b))*((i+1)%2==0?r:r*.2f);
            mesh.Triangle(Vector2.zero,p*1.35f,q*1.35f,mint,.01f);
            mesh.Triangle(Vector2.zero,p,q,gold,0);mesh.Triangle(Vector2.zero,p*.65f,q*.65f,cream,-.01f);
        }
        for(int i=0;i<5;i++)
        {
            float angle=i*Mathf.PI*2/5+.3f;
            var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            mesh.Diamond(axis*r*(1.2f+t),new Vector2(r*.07f,r*.14f),gold,-.02f);
        }
        mesh.End();
    }
    void OnDestroy(){mesh?.Dispose();if(material)Destroy(material);}
}
