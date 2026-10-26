using System.Collections.Generic;
using UnityEngine;
namespace KingOfGraphic.MechBossMigration
{
    public sealed class MechShieldEnvelope:MonoBehaviour
    {
        public Transform shell;
        SkinnedMeshRenderer[] body;Mesh baked;readonly List<Vector3> vertices=new List<Vector3>();float nextFit;
        void OnEnable(){body=GetComponentInParent<MechSentinelBattle>().GetComponentsInChildren<SkinnedMeshRenderer>();baked=new Mesh{name="Temporary shield fit"};Fit();}
        void LateUpdate(){if(Time.time>=nextFit)Fit();}
        public void Fit()
        {
            if(body==null||body.Length==0||!shell)return;nextFit=Time.time+.1f;
            var bounds=body[0].bounds;foreach(var r in body)if(r.enabled&&r.gameObject.activeInHierarchy)bounds.Encapsulate(r.bounds);
            var radii=bounds.extents+Vector3.one*.2f;float factor=1;
            foreach(var r in body)
            {
                if(!r.enabled||!r.gameObject.activeInHierarchy)continue;r.BakeMesh(baked);baked.GetVertices(vertices);
                foreach(var vertex in vertices){var p=r.transform.TransformPoint(vertex)-bounds.center;factor=Mathf.Max(factor,new Vector3(p.x/radii.x,p.y/radii.y,p.z/radii.z).magnitude);}
            }
            shell.position=bounds.center;shell.rotation=Quaternion.identity;shell.localScale=Vector3.Scale(radii*(2*factor*1.03f),new Vector3(1/transform.lossyScale.x,1/transform.lossyScale.y,1/transform.lossyScale.z));
        }
        void OnDisable(){if(baked)Destroy(baked);baked=null;}
    }
}
