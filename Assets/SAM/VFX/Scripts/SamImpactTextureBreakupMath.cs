using System;
using System.Collections.Generic;

namespace SamProduction
{
    // Pure managed production kernel: the offline harness executes this same partition and motion code.
    internal static class SamImpactTextureBreakupMath
    {
        internal struct Point
        {
            internal float x,y;
            internal Point(float x,float y){this.x=x;this.y=y;}
        }
        internal struct Motion
        {
            internal float birth,phase,reveal,forward,radialX,radialY,rise,spin,alongScale,acrossScale;
        }
        // Unequal sites give convex irregular polygons rather than regular square tiles.
        // The shader uses this same fixed list to feather/scallop polygon boundaries.
        internal static readonly Point[] Sites={new Point(.13f,.14f),new Point(.46f,.12f),new Point(.83f,.17f),
            new Point(.18f,.48f),new Point(.53f,.46f),new Point(.84f,.53f),
            new Point(.14f,.84f),new Point(.48f,.80f),new Point(.82f,.86f)};

        internal static Point[][] Partition()
        {
            var cells=new Point[Sites.Length][];
            for(int i=0;i<Sites.Length;i++)
            {
                var polygon=new List<Point>{new Point(0,0),new Point(1,0),new Point(1,1),new Point(0,1)};
                Point site=Sites[i];
                for(int j=0;j<Sites.Length;j++)
                {
                    if(i==j)continue;
                    Point other=Sites[j];
                    float nx=other.x-site.x,ny=other.y-site.y;
                    float limit=(other.x*other.x+other.y*other.y-site.x*site.x-site.y*site.y)*.5f;
                    var clipped=new List<Point>();
                    for(int k=0;k<polygon.Count;k++)
                    {
                        Point a=polygon[k],b=polygon[(k+1)%polygon.Count];
                        float da=a.x*nx+a.y*ny-limit,db=b.x*nx+b.y*ny-limit;
                        bool inA=da<=.0000001f,inB=db<=.0000001f;
                        if(inA)clipped.Add(a);
                        if(inA!=inB)
                        {
                            float t=da/(da-db);
                            clipped.Add(new Point(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t));
                        }
                    }
                    polygon=clipped;
                }
                cells[i]=polygon.ToArray();
            }
            return cells;
        }

        internal static Motion Evaluate(int piece,int lobe,int seed,float age,float splitAge,float strength,bool thin)
        {
            float start=Clamp(splitAge,.04f,.22f);
            float intensity=Clamp(strength,0,2);
            float a=Hash(piece+53+lobe*97,seed),b=Hash(piece+139+lobe*47,seed);
            float birth=start+a*.016f;
            float elapsed=Math.Max(0,age-birth);
            float phase=Clamp(elapsed/(thin?.145f:.175f),0,1);
            float reveal=Smooth(Clamp(elapsed/.032f,0,1));
            if(intensity<=0)reveal=0;
            // The analytic displacement has zero initial velocity; handoff cannot teleport a cell.
            float travel=elapsed-.022f*(1-(float)Math.Exp(-elapsed/.022f));
            float angle=piece*2.399963f+a*1.2f+lobe*.63f;
            float outward=(.65f+.9f*b)*travel*intensity*(thin?1.35f:1);
            return new Motion
            {
                birth=birth,phase=phase,reveal=reveal,
                forward=(1.35f+1.9f*a)*travel*intensity*(thin?1.4f:1),
                radialX=(float)Math.Cos(angle)*outward,radialY=(float)Math.Sin(angle)*outward,
                rise=(.35f+.55f*b)*travel*intensity,
                spin=(a-.5f)*2.1f*phase*intensity,
                alongScale=1-phase*(thin?.13f:.34f),
                acrossScale=1-phase*(thin?.80f:.54f)
            };
        }
        internal static float CoherentWeight(float age,float splitAge,float strength)
        {
            return strength<=0?1:1-Smooth(Clamp((age-Clamp(splitAge,.04f,.22f))/.048f,0,1));
        }
        internal static float ConeFade(float projectedDirectionLength)
        {
            return Smooth(Clamp((projectedDirectionLength-.052335956f)/(.13917310f-.052335956f),0,1));
        }
        internal static float Clamp(float value,float low,float high){return Math.Max(low,Math.Min(high,value));}
        internal static float Smooth(float x){return x*x*(3-2*x);}
        internal static float Hash(int index,int seed)
        {
            unchecked{uint n=(uint)(index*374761393)^(uint)seed;n=(n^(n>>13))*1274126177u;n^=n>>16;return (n&0x00ffffff)/16777215f;}
        }
    }
}
