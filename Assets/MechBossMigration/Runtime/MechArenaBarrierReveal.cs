using UnityEngine;
namespace KingOfGraphic.MechBossMigration
{
    public sealed class MechArenaBarrierReveal:MonoBehaviour
    {
        public Transform visual;public float riseDuration=.9f;public float wallHeight=9;public float baseOffset;float elapsed;
        void OnEnable(){elapsed=0;Apply(0);}
        void Update()=>AdvanceReveal(Time.deltaTime);
        public void AdvanceReveal(float dt){elapsed+=dt;float t=Mathf.Clamp01(elapsed/riseDuration);Apply(t*t*(3-2*t));}
        void Apply(float t)
        {
            if(!visual)return;float scale=Mathf.Max(.001f,t);
            visual.localScale=new Vector3(1,scale,1);
            var position=visual.localPosition;position.y=baseOffset+wallHeight*.5f*scale;visual.localPosition=position;
        }
    }
}
