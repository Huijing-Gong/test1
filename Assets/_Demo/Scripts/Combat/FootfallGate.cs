using UnityEngine;

// A contact must follow a lift and actual capsule travel. Animation alone cannot make a step.
public sealed class FootfallGate
{
    bool primed,leftArmed,rightArmed;
    float leftLow,rightLow,travel,nextTime;
    public float MinimumTravel=.12f, MinimumInterval=.14f;
    public void Reset(){primed=false;leftArmed=rightArmed=false;travel=0;nextTime=0;}
    public int Observe(float left,float right,float distance,float time,bool eligible)
    {
        if(!eligible||distance>2f||distance<0){Reset();return 0;}
        if(!primed){leftLow=left;rightLow=right;primed=true;}
        leftLow=Mathf.Min(leftLow,left);rightLow=Mathf.Min(rightLow,right);
        if(distance<.0001f){leftArmed=rightArmed=false;travel=0;return 0;}
        travel+=distance;
        if(left>leftLow+.05f)leftArmed=true;
        if(right>rightLow+.05f)rightArmed=true;
        bool leftContact=leftArmed&&left<=leftLow+.025f;
        bool rightContact=rightArmed&&right<=rightLow+.025f;
        if(leftContact)leftArmed=false;if(rightContact)rightArmed=false;
        if((!leftContact&&!rightContact)||travel<MinimumTravel||time<nextTime)return 0;
        travel=0;nextTime=time+MinimumInterval;return leftContact?1:2;
    }
}
