using UnityEngine;
using System.Collections.Generic;

namespace WayfarerInventory
{
    public sealed class CharacterPreview : MonoBehaviour
    {
        public GameObject visualPrefab;
        public AnimationClip idlePose;
        public RenderTexture Image { get; private set; }
        GameObject stage, visual;
        Camera portrait;
        Transform weapon;
        readonly List<Material> previewMaterials = new List<Material>();
        float yaw = 180;

        System.Collections.IEnumerator Start()
        {
            if (!visualPrefab) yield break;
            stage = new GameObject("Isolated character portrait stage");
            stage.transform.position = new Vector3(1000,1000,1000);
            visual = Instantiate(visualPrefab,stage.transform);
            visual.transform.localPosition = Vector3.zero;
            foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
            {
                animator.applyRootMotion = false;
                if (idlePose) idlePose.SampleAnimation(animator.gameObject,0);
                animator.enabled = false;
            }
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = 30;
                if (t.name == "PracticeSword") {weapon=t;weapon.gameObject.SetActive(false);}
            }
            visual.transform.localRotation=Quaternion.Euler(0,yaw,0);
            // Preview-only copies preserve the source albedo, independent of world lights.
            var unlit=Shader.Find("Universal Render Pipeline/Unlit");
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var sources=renderer.sharedMaterials;
                var copies=new Material[sources.Length];
                for(int i=0;i<sources.Length;i++)
                {
                    var source=sources[i];
                    if(!source)continue;
                    var copy=new Material(unlit){name=source.name+" (portrait only)"};
                    string map=source.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
                    if(source.HasProperty(map))
                    {
                        copy.SetTexture("_BaseMap",source.GetTexture(map));
                        copy.SetTextureScale("_BaseMap",source.GetTextureScale(map));
                        copy.SetTextureOffset("_BaseMap",source.GetTextureOffset(map));
                    }
                    copy.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.color);
                    copies[i]=copy;previewMaterials.Add(copy);
                }
                renderer.sharedMaterials=copies;
            }
            // Let Unity update the sampled skin's world bounds before framing it.
            yield return null;
            var bounds=new Bounds(visual.transform.position+Vector3.up,Vector3.one);
            bool first=true;
            foreach(var r in visual.GetComponentsInChildren<Renderer>())
            {
                if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);
            }
            portrait=new GameObject("Character portrait camera").AddComponent<Camera>();
            portrait.transform.SetParent(stage.transform);
            portrait.transform.position=bounds.center+new Vector3(0,.25f,-5);
            portrait.transform.LookAt(bounds.center);
            portrait.orthographic=true;portrait.orthographicSize=Mathf.Max(.5f,bounds.extents.y*1.12f);
            portrait.cullingMask=1<<30;portrait.clearFlags=CameraClearFlags.SolidColor;
            portrait.allowHDR=false;
            portrait.backgroundColor=new Color32(15,36,32,0);
            Image=new RenderTexture(720,900,24){name="Character portrait"};
            portrait.targetTexture=Image;

        }
        public void Rotate(float delta)
        {
            yaw+=delta;
            if(visual)visual.transform.localRotation=Quaternion.Euler(0,yaw,0);
        }
        public void Show(bool visible,bool swordEquipped)
        {
            if(portrait)portrait.enabled=visible;
            if(weapon)weapon.gameObject.SetActive(swordEquipped);
        }
        void OnDestroy()
        {
            if(stage)Destroy(stage);
            if(Image){Image.Release();Destroy(Image);}
            foreach(var material in previewMaterials)if(material)Destroy(material);
        }
    }
}
