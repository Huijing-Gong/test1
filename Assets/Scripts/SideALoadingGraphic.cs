using UnityEngine;
using UnityEngine.UI;

// Small vector meshes: no bitmap atlas, runtime texture, external shader or GIF.
public sealed class SideALoadingGraphic : MaskableGraphic
{
    public enum Shape { Crest, Compass, Chip, Diamond, Rule }
    public Shape shape;
    public bool filled;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        switch(shape)
        {
            case Shape.Rule: Polygon(vh,new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)},color);break;
            case Shape.Diamond: Diamond(vh,Vector2.zero,.88f,filled,color);break;
            case Shape.Chip:
                var p=new[]{new Vector2(-.88f,-1),new Vector2(.88f,-1),new Vector2(1,0),new Vector2(.88f,1),new Vector2(-.88f,1),new Vector2(-1,0)};
                Polygon(vh,p,color);for(int i=0;i<p.Length;i++)Line(vh,p[i]*.94f,p[(i+1)%p.Length]*.94f,1.3f,Gold);break;
            case Shape.Compass:
                Diamond(vh,Vector2.zero,.87f,false,color);Diamond(vh,Vector2.zero,.64f,false,color);
                for(int i=0;i<12;i++){float a=i*Mathf.PI/6;var d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Line(vh,d*.35f,d*(i%3==0?1:.80f),.7f,color);}
                break;
            case Shape.Crest:
                Diamond(vh,Vector2.zero,.85f,false,color);Diamond(vh,Vector2.zero,.80f,false,Gold);
                Platform(vh,new Vector2(-.34f,.10f),.26f);Platform(vh,new Vector2(.34f,.10f),.26f);Platform(vh,new Vector2(0,-.31f),.39f);
                Diamond(vh,new Vector2(0,.44f),.10f,true,Gold);Diamond(vh,new Vector2(0,-.94f),.045f,true,Gold);
                Line(vh,new Vector2(0,.31f),new Vector2(0,.66f),1,Gold);break;
        }
    }
    static Color Gold => new Color32(207,165,73,255);
    void Platform(VertexHelper vh,Vector2 c,float w)
    {
        Polygon(vh,new[]{c+new Vector2(-w,0),c+new Vector2(w,0),c+new Vector2(w*.45f,-.09f),c+new Vector2(-w*.6f,-.09f)},Gold);
        Polygon(vh,new[]{c+new Vector2(-w*.8f,-.1f),c+new Vector2(w*.6f,-.1f),c+new Vector2(w*.30f,-.18f),c+new Vector2(0,-.32f),c+new Vector2(-w*.4f,-.18f)},color);
        Line(vh,c+new Vector2(-w,-.03f),c+new Vector2(w,-.03f),1.2f,Gold);
    }
    void Diamond(VertexHelper vh,Vector2 c,float r,bool fill,Color tint)
    {
        var p=new[]{c+Vector2.up*r,c+Vector2.right*r,c+Vector2.down*r,c+Vector2.left*r};
        if(fill)Polygon(vh,p,tint);else for(int i=0;i<4;i++)Line(vh,p[i],p[(i+1)%4],shape==Shape.Diamond?1.4f:1.6f,tint);
    }
    Vector2 Point(Vector2 p){var r=rectTransform.rect;return r.center+Vector2.Scale(p,r.size*.5f);}
    void Polygon(VertexHelper vh,Vector2[] points,Color tint)
    {
        int start=vh.currentVertCount;foreach(var p in points)vh.AddVert(Point(p),tint,Vector2.zero);
        for(int i=1;i<points.Length-1;i++)vh.AddTriangle(start,start+i,start+i+1);
    }
    void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color tint)
    {
        a=Point(a);b=Point(b);var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;int start=vh.currentVertCount;
        foreach(var p in new[]{a-n,b-n,b+n,a+n})vh.AddVert(p,tint,Vector2.zero);
        vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
    }
}
