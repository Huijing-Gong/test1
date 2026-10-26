using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    // A forward-only geometric candidate. The body follows the derivative of
    // the actual root curve. Foot contacts and collision still require the
    // full motor preflight and executed checks; a curve alone proves neither.
    [Serializable]
    public sealed class ArenaForwardPatrolCurve
    {
        [Serializable]
        private sealed class Segment
        {
            public Vector3 a, b, c, d;
            public float length;
            private readonly float[] distances = new float[129];
            public Segment(Vector3 start, Vector3 end, Vector3 forwardStart, Vector3 forwardEnd, float handle)
            {
                a=start;d=end;b=a+forwardStart*handle;c=d-forwardEnd*handle;
                Vector3 previous=a;
                for(int i=1;i<distances.Length;i++)
                {
                    Vector3 current=Point(i/128f);
                    length+=Vector3.Distance(previous,current);distances[i]=length;previous=current;
                }
            }
            private Vector3 Point(float t)
            {
                float u=1-t;return u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;
            }
            private float Parameter(float progress)
            {
                float distance=Mathf.Clamp01(progress)*length;
                int lo=0,hi=128;
                while(hi-lo>1){int m=(lo+hi)/2;if(distances[m]<distance)lo=m;else hi=m;}
                return (lo+Mathf.InverseLerp(distances[lo],distances[hi],distance))/128f;
            }
            public Vector3 Position(float progress)=>Point(Parameter(progress));
            public Vector3 Tangent(float progress)
            {
                float t=Parameter(progress),u=1-t;
                return 3*u*u*(b-a)+6*u*t*(c-b)+3*t*t*(d-c);
            }
        }
        private Segment first, second;
        private ArcLineArc turning;
        private ArenaForwardPatrolCurve joinedFirst,joinedSecond;
        private ArenaForwardPatrolCurve tailCurve;
        private ArenaForwardPatrolCurve headCurve;
        private Vector3 headStart,headEnd;
        private Quaternion headHeading;
        private float headLength;
        private Vector3 tailStart,tailEnd;
        private Quaternion tailHeading;
        private float tailLength;
        private ArenaForwardPatrolCurve spliceSource,spliceTail;
        private float spliceProgress,spliceSplit,spliceSlope;
        public float Length => spliceSource!=null?spliceSource.Length*spliceProgress+spliceTail.Length:headCurve!=null?headLength+headCurve.Length:tailCurve!=null?tailCurve.Length+tailLength:joinedFirst!=null?joinedFirst.Length+joinedSecond.Length:turning!=null?turning.length:first.length+(second==null?0:second.length);
        public float FirstFraction => joinedFirst!=null?joinedFirst.Length/Length:second==null?1:first.length/Length;
        public bool HasReturn => second!=null||joinedFirst!=null;
        private ArenaForwardPatrolCurve(){}
        public static ArenaForwardPatrolCurve ReplaceFuture(ArenaForwardPatrolCurve prior,float progress,float split,ArenaForwardPatrolCurve future)
        {
            float before=Mathf.Max(0,progress-.0001f),after=Mathf.Min(1,progress+.0001f);
            float derivative=Vector3.Distance(prior.Position(before,split),prior.Position(after,split))/(after-before);
            float slope=derivative*(1-progress)/future.Length;
            if(progress<=0||progress>=1||slope<.5f||slope>2) return null;
            return new ArenaForwardPatrolCurve{spliceSource=prior,spliceTail=future,spliceProgress=progress,spliceSplit=split,spliceSlope=slope};
        }
        private float FutureProgress(float progress)
        {
            float s=Mathf.Clamp01((progress-spliceProgress)/(1-spliceProgress));
            return s+(spliceSlope-1)*s*(1-s)*(1-s);
        }
        public static ArenaForwardPatrolCurve Join(ArenaForwardPatrolCurve first,ArenaForwardPatrolCurve second)
        {return new ArenaForwardPatrolCurve{joinedFirst=first,joinedSecond=second};}
        public static ArenaForwardPatrolCurve WithStraightTail(ArenaForwardPatrolCurve curve,Vector3 start,Vector3 end,Quaternion heading)
        {return new ArenaForwardPatrolCurve{tailCurve=curve,tailStart=start,tailEnd=end,tailHeading=heading,tailLength=Vector3.Distance(start,end)};}
        public static ArenaForwardPatrolCurve WithStraightHead(ArenaForwardPatrolCurve curve,Vector3 start,Vector3 end,Quaternion heading)
        {return new ArenaForwardPatrolCurve{headCurve=curve,headStart=start,headEnd=end,headHeading=heading,headLength=Vector3.Distance(start,end)};}
        private sealed class ArcLineArc
        {
            public Vector3 origin,endpoint,centre1,centre2,tangent1,tangent2;
            public float radius,startYaw,straightYaw,endYaw,arc1,arc2,line,length;
            public int side1,side2;
            public void Sample(float progress,out Vector3 position,out Quaternion heading)
            {
                float distance=Mathf.Clamp01(progress)*length;
                float yaw;
                if(distance<=arc1)
                {
                    yaw=startYaw+side1*distance/radius*Mathf.Rad2Deg;
                    heading=Quaternion.Euler(0,yaw,0);position=centre1-heading*Vector3.right*radius*side1;
                }
                else if(distance<=arc1+line)
                {
                    heading=Quaternion.Euler(0,straightYaw,0);position=tangent1+heading*Vector3.forward*(distance-arc1);
                }
                else
                {
                    yaw=straightYaw+side2*(distance-arc1-line)/radius*Mathf.Rad2Deg;
                    heading=Quaternion.Euler(0,yaw,0);position=centre2-heading*Vector3.right*radius*side2;
                }
                if(progress<=0)position=origin;
                if(progress>=1)position=endpoint;
            }
        }
        public static bool TryArcLineArc(Vector3 origin,Quaternion initial,Vector3 endpoint,Quaternion final,
            float radius,int firstSide,int secondSide,out ArenaForwardPatrolCurve result)
        {
            result=null;
            var turn=new ArcLineArc{origin=origin,endpoint=endpoint,radius=radius,side1=firstSide,side2=secondSide,
                startYaw=initial.eulerAngles.y,endYaw=final.eulerAngles.y};
            turn.centre1=origin+initial*Vector3.right*(radius*firstSide);
            turn.centre2=endpoint+final*Vector3.right*(radius*secondSide);
            Vector3 centres=turn.centre2-turn.centre1;
            float separation=centres.magnitude,offset=radius*(secondSide-firstSide);
            if(separation<.0001f||separation<Mathf.Abs(offset)+.0001f)return false;
            turn.line=Mathf.Sqrt(separation*separation-offset*offset);
            turn.straightYaw=Mathf.Atan2(centres.x,centres.z)*Mathf.Rad2Deg-Mathf.Atan2(offset,turn.line)*Mathf.Rad2Deg;
            Quaternion tangent=Quaternion.Euler(0,turn.straightYaw,0);
            turn.tangent1=turn.centre1-tangent*Vector3.right*(radius*firstSide);
            turn.tangent2=turn.centre2-tangent*Vector3.right*(radius*secondSide);
            turn.arc1=PositiveDegrees((turn.straightYaw-turn.startYaw)*firstSide)*Mathf.Deg2Rad*radius;
            turn.arc2=PositiveDegrees((turn.endYaw-turn.straightYaw)*secondSide)*Mathf.Deg2Rad*radius;
            turn.length=turn.arc1+turn.line+turn.arc2;
            if(Vector3.Distance(turn.tangent1+tangent*Vector3.forward*turn.line,turn.tangent2)>.0001f)return false;
            result=new ArenaForwardPatrolCurve{turning=turn};return true;
        }
        private static float PositiveDegrees(float angle)
        {float value=Mathf.Repeat(angle,360);return value>359.999f?0:value;}
        public ArenaForwardPatrolCurve(Vector3 origin, Quaternion initial, Vector3 prior, Vector3 endpoint,
            Quaternion final, bool returning, float handleScale, int joinSide=1)
        {
            Vector3 startForward=initial*Vector3.forward,endForward=final*Vector3.forward;
            if(returning)
            {
                Vector3 incoming=(prior-origin).normalized,outgoing=(endpoint-prior).normalized;
                Vector3 joinForward=incoming+outgoing;
                if(joinForward.sqrMagnitude<.001f)joinForward=Vector3.Cross(Vector3.up,incoming);
                joinForward=joinForward.normalized*joinSide;
                // A reversal at B needs a real curved approach/departure.
                // Collinear opposite endpoint tangents produce a cusp instead.
                first=new Segment(origin,prior,startForward,joinForward,Mathf.Max(.55f,Vector3.Distance(origin,prior)*handleScale));
                second=new Segment(prior,endpoint,joinForward,endForward,Mathf.Max(.3f,Vector3.Distance(prior,endpoint)*handleScale));
            }
            else first=new Segment(origin,endpoint,startForward,endForward,Mathf.Max(.3f,Vector3.Distance(origin,endpoint)*handleScale));
        }
        private Segment Select(float progress,float split,out float local)
        {
            progress=Mathf.Clamp01(progress);
            if(second==null){local=progress;return first;}
            if(progress<=split){local=progress/split;return first;}
            local=(progress-split)/(1-split);return second;
        }
        public Vector3 Position(float progress,float split)
        {
            if(spliceSource!=null)return progress<=spliceProgress?spliceSource.Position(progress,spliceSplit):spliceTail.Position(FutureProgress(progress),0);
            if(headCurve!=null)
            {
                float distance=Mathf.Clamp01(progress)*Length;
                return distance<=headLength?Vector3.Lerp(headStart,headEnd,distance/headLength):headCurve.Position((distance-headLength)/headCurve.Length,0);
            }
            if(tailCurve!=null)
            {
                float distance=Mathf.Clamp01(progress)*Length;
                return distance<=tailCurve.Length?tailCurve.Position(distance/tailCurve.Length,0):Vector3.Lerp(tailStart,tailEnd,(distance-tailCurve.Length)/tailLength);
            }
            if(joinedFirst!=null)return progress<=split?joinedFirst.Position(progress/split,0):joinedSecond.Position((progress-split)/(1-split),0);
            if(turning!=null){turning.Sample(progress,out var position,out _);return position;}
            var segment=Select(progress,split,out float local);return segment.Position(local);
        }
        public Quaternion Heading(float progress,float split)
        {
            if(spliceSource!=null)return progress<=spliceProgress?spliceSource.Heading(progress,spliceSplit):spliceTail.Heading(FutureProgress(progress),0);
            if(headCurve!=null)
            {float distance=Mathf.Clamp01(progress)*Length;return distance<=headLength?headHeading:headCurve.Heading((distance-headLength)/headCurve.Length,0);}
            if(tailCurve!=null)
            {float distance=Mathf.Clamp01(progress)*Length;return distance<=tailCurve.Length?tailCurve.Heading(distance/tailCurve.Length,0):tailHeading;}
            if(joinedFirst!=null)return progress<=split?joinedFirst.Heading(progress/split,0):joinedSecond.Heading((progress-split)/(1-split),0);
            if(turning!=null){turning.Sample(progress,out _,out var heading);return heading;}
            var segment=Select(progress,split,out float local);Vector3 tangent=segment.Tangent(local);
            if(tangent.sqrMagnitude<.000001f)throw new InvalidOperationException("Forward patrol curve has a cusp; no body direction is defined.");
            return Quaternion.LookRotation(tangent,Vector3.up);
        }
    }
}
