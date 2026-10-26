using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
[ExecuteAlways]
public sealed class OwnPlanarLake : MonoBehaviour
{
    public Material lakeMaterial;
    public float waterY=3.5f;
    public int resolution=512;
    Camera mirror;
    RenderTexture reflection;
    bool rendering;
    float nextTime;
    Vector3 lastPosition;
    Quaternion lastRotation;
    int lastCamera;
    void OnEnable(){RenderPipelineManager.beginCameraRendering+=Begin;}
    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering-=Begin;
        if(lakeMaterial){lakeMaterial.SetFloat("_PlanarAvailable",0);lakeMaterial.SetTexture("_PlanarReflectionTex",null);}
        if(mirror){mirror.targetTexture=null;if(Application.isPlaying)Destroy(mirror.gameObject);else DestroyImmediate(mirror.gameObject);}
        if(reflection){reflection.Release();if(Application.isPlaying)Destroy(reflection);else DestroyImmediate(reflection);}
    }
    void Begin(ScriptableRenderContext context,Camera camera)
    {
        if(rendering||!lakeMaterial||!camera||camera==mirror||camera.cameraType==CameraType.Reflection||camera.cameraType==CameraType.Preview||camera.transform.position.y<waterY+.15f)return;
        // Presentation-only cameras never see the water layer and need no lake reflection.
        if((camera.cullingMask & (1 << 4)) == 0)return;
        bool evidence=camera.name=="Evidence camera temporary";
        if(Application.isPlaying&&!evidence&&camera.cameraType!=CameraType.Game)return;
        bool cameraMoved=(camera.transform.position-lastPosition).sqrMagnitude>.5625f||Quaternion.Angle(camera.transform.rotation,lastRotation)>5f;
        if(!evidence&&!cameraMoved&&lastCamera==camera.GetInstanceID()&&Time.realtimeSinceStartup<nextTime)return;
        if(!mirror)
        {
            var g=new GameObject("Own lake reflection transient",typeof(Camera));g.hideFlags=HideFlags.HideAndDontSave;mirror=g.GetComponent<Camera>();mirror.enabled=false;
        }
        if(!reflection||reflection.width!=resolution)
        {
            if(reflection){reflection.Release();DestroyImmediate(reflection);}
            reflection=new RenderTexture(resolution,resolution,16,RenderTextureFormat.DefaultHDR){name="Own lake planar reflection 512",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            reflection.Create();
        }
        mirror.CopyFrom(camera);mirror.enabled=false;mirror.useOcclusionCulling=false;mirror.cullingMask=camera.cullingMask&~(1<<4);mirror.targetTexture=reflection;
        var extra=mirror.GetUniversalAdditionalCameraData();extra.requiresDepthTexture=false;extra.requiresColorTexture=false;extra.renderShadows=false;extra.renderPostProcessing=false;
        var m=Matrix4x4.identity;m.m11=-1;m.m13=2*waterY;
        mirror.transform.position=m.MultiplyPoint(camera.transform.position);
        mirror.transform.forward=Vector3.Scale(camera.transform.forward,new Vector3(1,-1,1));
        mirror.worldToCameraMatrix=camera.worldToCameraMatrix*m;
        var p=mirror.worldToCameraMatrix.MultiplyPoint(new Vector3(0,waterY+.08f,0));
        var n=mirror.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
        mirror.projectionMatrix=camera.CalculateObliqueMatrix(new Vector4(n.x,n.y,n.z,-Vector3.Dot(p,n)));
        bool oldCull=GL.invertCulling;rendering=true;
        try
        {
            GL.invertCulling=!oldCull;
#pragma warning disable 0618
            UniversalRenderPipeline.RenderSingleCamera(context,mirror);
#pragma warning restore 0618
            lakeMaterial.SetTexture("_PlanarReflectionTex",reflection);
            lakeMaterial.SetMatrix("_PlanarVP",GL.GetGPUProjectionMatrix(mirror.projectionMatrix,true)*mirror.worldToCameraMatrix);
            lakeMaterial.SetFloat("_PlanarAvailable",1);
            nextTime=Time.realtimeSinceStartup+.10f;lastCamera=camera.GetInstanceID();lastPosition=camera.transform.position;lastRotation=camera.transform.rotation;
        }
        finally{GL.invertCulling=oldCull;rendering=false;}
    }
}