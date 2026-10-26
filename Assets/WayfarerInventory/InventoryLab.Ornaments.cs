using UnityEngine;

namespace WayfarerInventory
{
    public sealed partial class InventoryLab
    {
        static readonly Color Mint=new Color32(185,225,192,255), Silhouette=new Color32(164,173,148,255);
        Texture2D softShadow;
        static Rect Inset(Rect r,float n)=>new Rect(r.x+n,r.y+n,r.width-2*n,r.height-2*n);
        static void Line(Vector2 a,Vector2 b,Color c,float thickness=1)
        {
            var matrix=GUI.matrix;
            GUI.matrix=matrix*Matrix4x4.Translate(a)*Matrix4x4.Rotate(Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg))*Matrix4x4.Translate(-a);
            Fill(new Rect(a.x,a.y,(b-a).magnitude,thickness),c);GUI.matrix=matrix;
        }
        static void Diamond(Vector2 p,float size,Color c)
        {
            var matrix=GUI.matrix;GUI.matrix=matrix*Matrix4x4.Translate(p)*Matrix4x4.Rotate(Quaternion.Euler(0,0,45))*Matrix4x4.Translate(-p);
            Fill(new Rect(p.x-size/2,p.y-size/2,size,size),c);GUI.matrix=matrix;
        }
        // Small radii soften the corners without changing the panel silhouette.
        static void RoundFill(Rect r,Color c,float radius=8)
        {
            radius=Mathf.Min(radius,Mathf.Min(r.width,r.height)/2);
            Fill(new Rect(r.x,r.y+radius,r.width,r.height-2*radius),c);
            int rows=Mathf.CeilToInt(radius);
            for(int i=0;i<rows;i++)
            {
                float dy=radius-i-.5f;
                float inset=radius-Mathf.Sqrt(Mathf.Max(0,radius*radius-dy*dy));
                Fill(new Rect(r.x+inset,r.y+i,r.width-2*inset,1),c);
                Fill(new Rect(r.x+inset,r.yMax-i-1,r.width-2*inset,1),c);
            }
        }
        static void RoundFrame(Rect r,Color c,float radius=8)
        {
            radius=Mathf.Min(radius,Mathf.Min(r.width,r.height)/2);
            Fill(new Rect(r.x+radius,r.y,r.width-2*radius,1),c);
            Fill(new Rect(r.x+radius,r.yMax-1,r.width-2*radius,1),c);
            Fill(new Rect(r.x,r.y+radius,1,r.height-2*radius),c);
            Fill(new Rect(r.xMax-1,r.y+radius,1,r.height-2*radius),c);
            Vector2[] centers={new Vector2(r.x+radius,r.y+radius),new Vector2(r.xMax-radius-1,r.y+radius),new Vector2(r.xMax-radius-1,r.yMax-radius-1),new Vector2(r.x+radius,r.yMax-radius-1)};
            for(int corner=0;corner<4;corner++)
            {
                float start=180+corner*90;
                Vector2 previous=centers[corner]+new Vector2(Mathf.Cos(start*Mathf.Deg2Rad),Mathf.Sin(start*Mathf.Deg2Rad))*radius;
                for(int step=1;step<=12;step++)
                {
                    float angle=(start+step*7.5f)*Mathf.Deg2Rad;
                    Vector2 next=centers[corner]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                    Line(previous,next,c);previous=next;
                }
            }
        }
        static void OrnamentRule(Rect r)
        {
            Fill(new Rect(r.x,r.center.y,r.width/2-12,1),Separator);
            Fill(new Rect(r.center.x+12,r.center.y,r.width/2-12,1),Separator);
            Diamond(r.center,6,Separator);
        }
        GUIStyle TextStyle(GUIStyle source,Color color,TextAnchor alignment=TextAnchor.MiddleLeft)
        {
            var s=new GUIStyle(source){alignment=alignment};s.normal.textColor=color;return s;
        }
        bool ActionButton(Rect r,string text)
        {
            bool hover=GUI.enabled && r.Contains(Event.current.mousePosition);
            RoundFill(r,hover?Muted:Ink);RoundFrame(Inset(r,1),Gold);RoundFrame(Inset(r,5),Gold,4);
            GUI.Label(r,text,TextStyle(button,Surface,TextAnchor.MiddleCenter));
            return GUI.Button(r,GUIContent.none,GUIStyle.none);
        }
        static void Check(Vector2 p,float size)
        {
            Diamond(p,size,Mint);
            Line(p+new Vector2(-size*.25f,0),p+new Vector2(-size*.05f,size*.2f),Ink,2);
            Line(p+new Vector2(-size*.05f,size*.2f),p+new Vector2(size*.3f,-size*.25f),Ink,2);
        }
        void GroundShadow(Rect r)
        {
            if(!softShadow)
            {
                softShadow=new Texture2D(128,64,TextureFormat.RGBA32,false){name="Portrait soft ground shadow",hideFlags=HideFlags.HideAndDontSave};
                for(int y=0;y<64;y++)for(int x=0;x<128;x++)
                {
                    float d=new Vector2((x-63.5f)/63.5f,(y-31.5f)/31.5f).magnitude;
                    softShadow.SetPixel(x,y,new Color(Ink.r,Ink.g,Ink.b,Mathf.Pow(Mathf.Clamp01(1-d),2)*.28f));
                }
                softShadow.Apply();
            }
            GUI.DrawTexture(r,softShadow);
        }
        void OnDestroy(){if(softShadow)Destroy(softShadow);}
        static void EmptySlotIcon(Rect r,int type)
        {
            Vector2 c=r.center;Color ink=Silhouette;
            if(type==0){Line(c+new Vector2(0,-20),c+new Vector2(0,20),ink,5);Line(c+new Vector2(-12,8),c+new Vector2(12,8),ink,4);}
            if(type==1){for(int y=0;y<34;y++){float w=y<20?27:27*(34-y)/14f;Fill(new Rect(c.x-w/2,c.y-16+y,w,1),ink);}}
            if(type==2 || type==3 || type==5)
            {
                float rx=type==5?10:17,ry=type==3?20:11;
                Vector2 last=c+new Vector2(rx,0);
                for(int n=1;n<=40;n++){float a=n*Mathf.PI*2/40;Vector2 next=c+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry);Line(last,next,ink,3);last=next;}
                if(type==2)Diamond(c+new Vector2(0,-16),8,ink);
                if(type==3)Diamond(c+new Vector2(0,24),10,ink);
            }
            if(type==4){for(int n=0;n<4;n++){float a=n*Mathf.PI/2;Diamond(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*11,12,ink);}}
        }
    }
}
