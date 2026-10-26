using UnityEngine;

// Shared bounded contact ink for the opponent/legacy combo callers. This replaces
// their per-hit sprite explosions, including the old high-% full-screen flash.
public sealed class BattleContactPresentation:MonoBehaviour
{
    static BattleContactPresentation instance;
    readonly Hit[] hits=new Hit[8];int cursor;Material material;BattleJPresentation.InkMesh mesh;
    struct Hit {public Vector3 point;public float age,life,power,direction;public bool enemy,upper,live;public int frame;}
    public static void Emit(Vector3 point,float facing,int step,bool enemy,bool upper)
    {
        if(!instance)instance=new GameObject("Shared contact ink pool").AddComponent<BattleContactPresentation>();
        point.z=-.72f;
        instance.hits[instance.cursor++%8]=new Hit{point=point,direction=facing,power=step==2?.44f:.30f,life=step==2?.20f:.14f,
            upper=upper,enemy=enemy,live=true,frame=Time.frameCount};
    }
    void Awake()
    {
        material=new Material(Resources.Load<Shader>("VFX/BattleJInk")){hideFlags=HideFlags.DontSave};
        mesh=new BattleJPresentation.InkMesh("Contact stars",transform,material);
    }
    void LateUpdate()
    {
        if(Time.timeScale<=0)return;
        mesh.Begin(Vector3.zero);
        for(int n=0;n<hits.Length;n++)
        {
            if(!hits[n].live)continue;
            if(hits[n].frame!=Time.frameCount)hits[n].age+=Mathf.Min(Time.unscaledDeltaTime,.033f);
            var hit=hits[n];float t=hit.age/hit.life;
            if(t>=1){hits[n].live=false;continue;}
            Vector2 center=new Vector2(hit.point.x,hit.point.y);
            float r=hit.power*Mathf.Lerp(.7f,1.2f,Mathf.Sqrt(t)),alpha=(1-t)*(1-t);
            for(int layer=0;layer<3;layer++)
            {
                float scale=layer==0?1.13f:layer==1?1:.65f;
                var color=layer==0?new Color(.075f,.216f,.192f,alpha):layer==1?new Color(.92f,.75f,.33f,alpha):new Color(.97f,.93f,.80f,alpha);
                for(int i=0;i<8;i++)
                {
                    float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;
                    Vector2 p=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r*scale*(i%2==0?1:.22f);
                    Vector2 q=new Vector2(Mathf.Cos(b),Mathf.Sin(b))*r*scale*((i+1)%2==0?1:.22f);
                    mesh.Triangle(center,center+p,center+q,color,hit.point.z-layer*.006f);
                }
            }
            for(int i=0;i<3;i++)
            {
                float a=(i-1)*.8f;
                Vector2 dir=hit.upper?new Vector2(Mathf.Sin(a),Mathf.Cos(a)):new Vector2(hit.direction*Mathf.Cos(a),Mathf.Sin(a));
                mesh.Diamond(center+dir*(r*.9f+t*.45f),new Vector2(.03f,.055f)*(1-t),
                    hit.enemy?new Color(.85f,.38f,.3f,alpha*.65f):new Color(.52f,.8f,.77f,alpha*.65f),hit.point.z-.025f);
            }
        }
        mesh.End();
    }
    void OnDestroy(){if(instance==this)instance=null;mesh?.Dispose();if(material)Destroy(material);}
}
