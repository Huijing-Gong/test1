using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Visual-only existing avatar, without gameplay scripts, lights or physics.
public sealed class BaselineTutorialPreview : MonoBehaviour
{
    public RenderTexture Texture { get; private set; }
    GameObject stage, model;
    Camera previewCamera;
    Animator animator;
    BaselineCampTutorial.Lesson lesson;
    float nextRender;
    float shownAt;
    int demoCycle = -1;
    bool pullUpTriggered;
    public int DemoDirection => Mathf.FloorToInt(Time.unscaledTime / 1.6f) % 4;
    public void Show(Animator source, BaselineCampTutorial.Lesson action)
    {
        lesson = action;
        if (!stage && source)
        {
            stage = new GameObject("Tutorial visual stage") { hideFlags=HideFlags.DontSave };
            stage.SetActive(false); stage.transform.SetParent(transform,false);
            stage.transform.position=new Vector3(2100,50,2100);
            model = Instantiate(source.gameObject,stage.transform); model.name="Tutorial avatar (visual only)";
            foreach (var b in model.GetComponentsInChildren<MonoBehaviour>(true)) { b.enabled=false; Destroy(b); }
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) { c.enabled=false; Destroy(c); }
            foreach (var b in model.GetComponentsInChildren<Rigidbody>(true)) Destroy(b);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=31;
            model.transform.localPosition=Vector3.zero; model.transform.localRotation=Quaternion.Euler(0,155,0);
            model.transform.localScale=source.transform.lossyScale;
            animator=model.GetComponent<Animator>(); animator.applyRootMotion=false;
            animator.updateMode=AnimatorUpdateMode.UnscaledTime; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false; }
            var co=new GameObject("Tutorial preview camera",typeof(Camera)); co.transform.SetParent(stage.transform,false);
            previewCamera=co.GetComponent<Camera>(); previewCamera.enabled=false;
            previewCamera.clearFlags=CameraClearFlags.SolidColor; previewCamera.backgroundColor=new Color(.065f,.11f,.14f);
            previewCamera.cullingMask=1<<31; previewCamera.orthographic=true; previewCamera.orthographicSize=1.2f;
            previewCamera.nearClipPlane=.1f; previewCamera.farClipPlane=12; previewCamera.allowHDR=false; previewCamera.allowMSAA=false;
            co.transform.localPosition=new Vector3(0,1,4); co.transform.localRotation=Quaternion.Euler(0,180,0);
            var data=co.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing=false; data.renderShadows=false;
            data.requiresColorTexture=false; data.requiresDepthTexture=false; data.volumeLayerMask=0;
            Texture=new RenderTexture(256,192,16,RenderTextureFormat.ARGB32) { name="Opening lesson preview", hideFlags=HideFlags.DontSave };
            Texture.Create(); previewCamera.targetTexture=Texture;
        }
        if (!stage) return;
        stage.SetActive(true); animator.Rebind();
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0,155,0);
        shownAt = Time.unscaledTime; demoCycle = -1; pullUpTriggered = false;
        bool swimming=action==BaselineCampTutorial.Lesson.Swim;
        animator.SetBool("IsGrounded",!swimming); animator.SetBool("IsSwimming",swimming);
        animator.SetFloat("SwimAmount",swimming?.65f:0);
        animator.SetBool("IsClimbing", action == BaselineCampTutorial.Lesson.Climb);
        animator.SetFloat("ClimbAmount", action == BaselineCampTutorial.Lesson.Climb ? .7f : 0);
        animator.SetFloat("VerticalSpeed", 0);
        animator.SetFloat("MoveAmount",action==BaselineCampTutorial.Lesson.Move?.5f:0); nextRender=0;
    }
    public void Hide() { if(stage)stage.SetActive(false); }
    void LateUpdate()
    {
        if (!stage || !stage.activeSelf) return;
        if (lesson==BaselineCampTutorial.Lesson.Look) model.transform.localRotation=Quaternion.Euler(0,155+Mathf.Sin(Time.unscaledTime*1.5f)*38,0);
        else if (lesson==BaselineCampTutorial.Lesson.Move) model.transform.localRotation=Quaternion.Euler(0,DemoDirection==0?180:DemoDirection==1?90:DemoDirection==2?0:270,0);
        else if (lesson == BaselineCampTutorial.Lesson.Jump || lesson == BaselineCampTutorial.Lesson.Drop)
        {
            float elapsed = Time.unscaledTime - shownAt;
            int cycle = Mathf.FloorToInt(elapsed / 2.8f);
            float phase = elapsed % 2.8f;
            bool jump = lesson == BaselineCampTutorial.Lesson.Jump;
            if (cycle != demoCycle)
            {
                demoCycle = cycle;
                if (jump) animator.SetTrigger("Jump");
            }
            bool airborne = (jump || phase > .18f) && phase < 1.25f;
            animator.SetBool("IsGrounded", !airborne);
            animator.SetFloat("VerticalSpeed", airborne ? (jump && phase < .7f ? 3 : -3) : 0);
            // A visual demonstration, using the existing avatar animation;
            // this preview is intentionally not a second physics simulation.
            float height = !airborne ? 0 : jump ? Mathf.Sin(Mathf.Clamp01((phase - .18f) / 1.07f) * Mathf.PI) * .45f :
                Mathf.Lerp(.45f, 0, (phase - .18f) / 1.07f);
            model.transform.localPosition = Vector3.up * height;
        }
        else if (lesson == BaselineCampTutorial.Lesson.Climb)
        {
            float elapsed = Time.unscaledTime - shownAt;
            int cycle = Mathf.FloorToInt(elapsed / 5f);
            float phase = elapsed % 5f;
            if (cycle != demoCycle)
            {
                demoCycle = cycle; pullUpTriggered = false;
                animator.Rebind(); animator.SetBool("IsGrounded", false); animator.SetBool("IsClimbing", true);
            }
            animator.SetFloat("ClimbAmount", phase < 1.2f ? .7f : 0);
            if (phase >= 1.2f && !pullUpTriggered)
            { pullUpTriggered = true; animator.SetTrigger("ClimbTop"); }
            model.transform.localPosition = Vector3.up * Mathf.Min(.35f, phase * .15f);
        }
        if (Time.unscaledTime<nextRender)return;
        nextRender=Time.unscaledTime+1f/24; previewCamera.Render();
    }
    void OnDisable() { Hide(); }
    void OnDestroy()
    {
        if(stage)Destroy(stage);
        if(Texture) { Texture.Release(); Destroy(Texture); }
    }
}

