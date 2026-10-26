using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// F46: scene reflection is renderer-local; never serialize transient RT/ready state into a shared asset.
[ExecuteAlways]
public sealed class OwnPlanarLakeF46 : MonoBehaviour
{
    public Renderer lakeRenderer;
    public float waterY=3.5f;
    public int resolution=512;
    [Min(.05f)] public float refreshSeconds=.10f;
    Camera mirror; RenderTexture reflection; MaterialPropertyBlock block;
    bool rendering; float nextTime; Vector3 lastPosition; Quaternion lastRotation; int lastCamera;
    public int ReflectionRenderCount { get; private set; }
    public bool ReflectionReady { get { return reflection && reflection.IsCreated(); } }
    static readonly int Ready=Shader.PropertyToID("_PlanarAvailable");
    static readonly int Tex=Shader.PropertyToID("_PlanarReflectionTex");
    static readonly int VP=Shader.PropertyToID("_PlanarVP");
    void OnEnable(){RenderPipelineManager.beginCameraRendering+=Begin;}
    void WriteState(bool ready,Matrix4x4 matrix)
    {
        if(!lakeRenderer)return;
        if(block==null)block=new MaterialPropertyBlock();
        lakeRenderer.GetPropertyBlock(block);
        block.SetFloat(Ready,ready?1:0);
        block.SetTexture(Tex,ready?(Texture)reflection:Texture2D.blackTexture);
        block.SetMatrix(VP,matrix);
        lakeRenderer.SetPropertyBlock(block);
    }
    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering-=Begin;
        WriteState(false,Matrix4x4.identity);
        if(mirror){mirror.targetTexture=null;if(Application.isPlaying)Destroy(mirror.gameObject);else DestroyImmediate(mirror.gameObject);}
        if(reflection){reflection.Release();if(Application.isPlaying)Destroy(reflection);else DestroyImmediate(reflection);}
        mirror=null; reflection=null;
    }
    void Begin(ScriptableRenderContext context,Camera camera)
    {
        if(rendering||!lakeRenderer||!camera||camera==mirror||camera.cameraType==CameraType.Reflection||camera.cameraType==CameraType.Preview||camera.transform.position.y<waterY+.15f)return;
        if((camera.cullingMask&(1<<4))==0)return;
        bool evidence=camera.name=="Evidence camera temporary";
        if(Application.isPlaying&&!evidence&&camera.cameraType!=CameraType.Game)return;
        bool moved=(camera.transform.position-lastPosition).sqrMagnitude>.5625f||Quaternion.Angle(camera.transform.rotation,lastRotation)>5;
        if(!evidence&&!moved&&lastCamera==camera.GetInstanceID()&&Time.realtimeSinceStartup<nextTime)return;
        if(!mirror){var g=new GameObject("Own lake reflection F46 transient",typeof(Camera));g.hideFlags=HideFlags.HideAndDontSave;mirror=g.GetComponent<Camera>();mirror.enabled=false;}
        if(!reflection||reflection.width!=resolution)
        {
            if(reflection){reflection.Release();if(Application.isPlaying)Destroy(reflection);else DestroyImmediate(reflection);}
            int size=Mathf.Clamp(resolution,128,2048);
            reflection=new RenderTexture(size,size,16,RenderTextureFormat.DefaultHDR){name="F46 renderer-local planar reflection",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
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
            WriteState(true,GL.GetGPUProjectionMatrix(mirror.projectionMatrix,true)*mirror.worldToCameraMatrix);
            nextTime=Time.realtimeSinceStartup+refreshSeconds;lastCamera=camera.GetInstanceID();lastPosition=camera.transform.position;lastRotation=camera.transform.rotation;ReflectionRenderCount++;
        }
        finally{GL.invertCulling=oldCull;rendering=false;}
    }
}
