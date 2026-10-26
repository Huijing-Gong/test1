using System.Collections.Generic;
using UnityEngine;
namespace KingOfGraphic.Hero
{
    public enum HeroTraversalKind { Ladder,Rope,Pole,Ledge,LowStep,Vault,Mantle,Seat,Scramble }
    [DisallowMultipleComponent] public sealed class HeroTraversalSurface:MonoBehaviour
    {
        public HeroTraversalKind kind;
        public Transform bottom,top;
        public float reach=1.5f,lateralExtent=1.1f;
        public Collider[] contactSolids;
        public bool topExit=true;
        // Optional measured motion profile for this particular mantle facility.
        public string mantleActionId;
        public static readonly List<HeroTraversalSurface> Available=new List<HeroTraversalSurface>();
        public Vector3 Bottom=>bottom?bottom.position:transform.position;
        public Vector3 Top=>top?top.position:transform.position+Vector3.up*2;
        void OnEnable(){if(!Available.Contains(this))Available.Add(this);}
        void OnDisable(){Available.Remove(this);}
    }
}
