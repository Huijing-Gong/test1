using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction.BossArena
{
    // Scene-owned ambient data is rebuilt after loading. No simulation, input or camera control.
    // CustomProvided also gives the character a deterministic diffuse environment when no
    // lighting asset was baked. Direct lights and their shadows still use the normal URP path.
    [DisallowMultipleComponent]
    public sealed class ArenaEnvironmentLight : MonoBehaviour
    {
        public SkinnedMeshRenderer[] actors = Array.Empty<SkinnedMeshRenderer>();
        public Vector3 diffuseFloorLinear = new Vector3(.14f, .16f, .19f);
        public Vector3 upperBounceLinear = new Vector3(.17f, .19f, .22f);
        [Range(0, 1)] public float upperBounceStrength = .30f;

        private static readonly string[] ShNames = { "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" };
        private static readonly Vector3[] Directions = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        private readonly Vector4[] packed = new Vector4[7];
        private readonly List<Snapshot> snapshots = new List<Snapshot>();
        private SphericalHarmonicsL2 expected;
        private bool configured;
        private string reportPath;
        private double started;
        private int nextSnapshot, ambientReapplications;
        private readonly double[] sampleTimes = { .5, 8.5, 9.5 };

        public void ApplyNow()
        {
            expected = BuildProbe();
            var conversion = new MaterialPropertyBlock();
            conversion.CopySHCoefficientArraysFrom(new[] { expected });
            for (int i=0; i<ShNames.Length; i++) packed[i] = conversion.GetVectorArray(ShNames[i])[0];

            // Trilight regenerates its own probe; Skybox is Unity's supported custom-SH mode.
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientProbe = expected;
            foreach (var actor in actors)
            {
                if (actor == null) continue;
                var block = new MaterialPropertyBlock();
                actor.GetPropertyBlock(block);
                // Set non-instanced float4 values from Unity's own SH packing conversion.
                for (int i=0; i<ShNames.Length; i++) block.SetVector(ShNames[i], packed[i]);
                actor.lightProbeUsage = LightProbeUsage.CustomProvided;
                actor.SetPropertyBlock(block);
            }
            configured = true;
        }

        public SphericalHarmonicsL2 BuildProbe()
        {
            var sh = new SphericalHarmonicsL2();
            sh.Clear();
            sh.AddAmbientLight(new Color(diffuseFloorLinear.x, diffuseFloorLinear.y, diffuseFloorLinear.z, 1));
            sh.AddDirectionalLight(Vector3.up, new Color(upperBounceLinear.x, upperBounceLinear.y, upperBounceLinear.z, 1), upperBounceStrength);
            return sh;
        }

        public float GlobalReadbackError() => ProbeError(RenderSettings.ambientProbe, expected);

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            ApplyNow();
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            started = Time.realtimeSinceStartupAsDouble;
            string requested = Arg("--sam-environment-report");
            if (string.IsNullOrEmpty(requested)) return;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--sam-benchmark") >= 0)
            {
                Debug.LogWarning("Environment mesh diagnostics disabled for --sam-benchmark; normal environment lighting remains enabled.");
                return;
            }
            try
            {
                if (!Path.IsPathRooted(requested)) throw new ArgumentException("Use an absolute --sam-environment-report JSON path.");
                reportPath = Path.GetFullPath(requested);
                if (File.Exists(reportPath)) throw new IOException("Environment evidence already exists; choose a new path.");
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                WriteReport(false);
            }
            catch (Exception error) { reportPath=null; Debug.LogError("Environment diagnostic output unavailable: " + error.Message); }
        }

        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= BeforeCamera; }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!configured || camera.cameraType != CameraType.Game) return;
            if (RenderSettings.ambientMode != AmbientMode.Skybox || GlobalReadbackError() > .00001f)
            {
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientProbe = expected;
                ambientReapplications++;
            }
        }

        private void LateUpdate()
        {
            if (reportPath == null || nextSnapshot >= sampleTimes.Length) return;
            double elapsed = Time.realtimeSinceStartupAsDouble - started;
            if (elapsed < sampleTimes[nextSnapshot]) return;
            try
            {
                snapshots.Add(Capture(elapsed));
                nextSnapshot++;
                WriteReport(nextSnapshot == sampleTimes.Length);
            }
            catch (Exception error)
            {
                Debug.LogError("Environment diagnostic capture failed: " + error);
                reportPath = null;
            }
        }

        private Snapshot Capture(double elapsed)
        {
            var records = new List<ActorRecord>();
            var camera = Camera.main;
            foreach (var actor in actors)
            {
                if (actor == null) continue;
                var record = new ActorRecord { name=actor.name, mesh=actor.sharedMesh.name,
                    vertexCount=actor.sharedMesh.vertexCount, subMeshCount=actor.sharedMesh.subMeshCount,
                    transformDeterminant=actor.localToWorldMatrix.determinant, bounds=actor.bounds,
                    lightProbeUsage=actor.lightProbeUsage.ToString(), shadowCasting=actor.shadowCastingMode.ToString(),
                    receiveShadows=actor.receiveShadows, renderingLayerMask=actor.renderingLayerMask };
                var block = new MaterialPropertyBlock();
                actor.GetPropertyBlock(block);
                record.suppliedShaderVectors = new Vector4[7];
                for (int i=0; i<7; i++)
                {
                    record.suppliedShaderVectors[i]=block.GetVector(ShNames[i]);
                    record.maximumShaderVectorReadbackError=Mathf.Max(record.maximumShaderVectorReadbackError,
                        (record.suppliedShaderVectors[i]-packed[i]).magnitude);
                }
                var materials=actor.sharedMaterials;
                record.materials=new MaterialRecord[materials.Length];
                for (int i=0; i<materials.Length; i++)
                {
                    var material=materials[i];
                    if (material == null) continue;
                    record.materials[i]=new MaterialRecord { rendererSlot=i, name=material.name, shader=material.shader.name,
                        baseColor=material.GetVector("_BaseColor"), emission=material.GetVector("_EmissionColor"),
                        metallic=material.GetFloat("_Metallic"), smoothness=material.GetFloat("_Smoothness"),
                        baseMap=TextureName(material,"_BaseMap"), normalMap=TextureName(material,"_BumpMap"),
                        occlusionMap=TextureName(material,"_OcclusionMap") };
                }
                var mesh=new Mesh { name="Temporary actual-pose normal measurement" };
                try
                {
                    actor.BakeMesh(mesh, false);
                    var positions=mesh.vertices;
                    var normals=mesh.normals;
                    var matrix=actor.localToWorldMatrix;
                    var normalMatrix=matrix.inverse.transpose;
                    var world=new Vector3[positions.Length];
                    var worldNormals=new Vector3[normals.Length];
                    record.minimumNormalLength=float.MaxValue;
                    for(int i=0;i<positions.Length;i++) world[i]=matrix.MultiplyPoint3x4(positions[i]);
                    for(int i=0;i<normals.Length;i++)
                    {
                        float length=normals[i].magnitude;
                        if (!Finite(normals[i])) { record.nonFiniteNormals++; continue; }
                        if(length<.00001f) record.zeroNormals++;
                        record.minimumNormalLength=Mathf.Min(record.minimumNormalLength,length);
                        record.maximumNormalLength=Mathf.Max(record.maximumNormalLength,length);
                        worldNormals[i]=normalMatrix.MultiplyVector(normals[i]).normalized;
                    }
                    if(normals.Length!=positions.Length) throw new InvalidDataException("Baked normal count differs from vertices.");
                    var regions=new List<RegionRecord>();
                    for(int slot=0;slot<mesh.subMeshCount;slot++)
                    {
                        if(mesh.GetTopology(slot)!=MeshTopology.Triangles) continue;
                        string actualMaterialName=slot<materials.Length && materials[slot]!=null ? materials[slot].name : "missing";
                        var rows=new[] { new RegionRecord { rendererSlot=slot, actualMaterialName=actualMaterialName, region="lower_0_to_62_percent_height" },
                            new RegionRecord { rendererSlot=slot, actualMaterialName=actualMaterialName, region="shoulder_chest_62_to_84_percent_height" },
                            new RegionRecord { rendererSlot=slot, actualMaterialName=actualMaterialName, region="head_84_to_100_percent_height" } };
                        var indices=mesh.GetTriangles(slot);
                        foreach(var row in rows) row.minimumGeometricNormalDot=1;
                        for(int t=0;t<indices.Length;t+=3)
                        {
                            int a=indices[t],b=indices[t+1],c=indices[t+2];
                            var center=(world[a]+world[b]+world[c])/3f;
                            float h=(center.y-actor.bounds.min.y)/Mathf.Max(.00001f,actor.bounds.size.y);
                            var row=rows[h>=.84f?2:h>=.62f?1:0];
                            var cross=Vector3.Cross(world[b]-world[a],world[c]-world[a]);
                            if(cross.sqrMagnitude<1e-14f) { row.degenerateTriangles++; continue; }
                            var geometric=cross.normalized;
                            var shading=(worldNormals[a]+worldNormals[b]+worldNormals[c]).normalized;
                            float dot=Vector3.Dot(geometric,shading);
                            row.triangles++;
                            row.minimumGeometricNormalDot=Mathf.Min(row.minimumGeometricNormalDot,dot);
                            row.meanGeometricNormalDot+=dot;
                            if(dot<-.25f) row.opposedGeometricNormals++;
                            if(Mathf.Abs(dot)<.25f) row.nearlyPerpendicularNormals++;
                            if(camera!=null && Vector3.Dot(geometric,camera.transform.position-center)>0) row.geometricFacesTowardCamera++;
                        }
                        foreach(var row in rows) { if(row.triangles>0) row.meanGeometricNormalDot/=row.triangles; regions.Add(row); }
                    }
                    record.regions=regions.ToArray();
                    record.shapeWeights=new float[actor.sharedMesh.blendShapeCount];
                    for(int i=0;i<record.shapeWeights.Length;i++) record.shapeWeights[i]=actor.GetBlendShapeWeight(i);
                }
                finally { Destroy(mesh); }
                records.Add(record);
            }
            return new Snapshot { elapsedSinceEnvironmentEnable=elapsed, unityRealtime=Time.realtimeSinceStartupAsDouble,
                unityFrame=Time.frameCount, ambientMode=RenderSettings.ambientMode.ToString(),
                actualGlobalCoefficients=Coefficients(RenderSettings.ambientProbe), globalReadbackError=GlobalReadbackError(),
                globalReapplications=ambientReapplications, actors=records.ToArray() };
        }

        private void WriteReport(bool complete)
        {
            var urp=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var directional=new List<LightRecord>();
            foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if(light.type==LightType.Directional) directional.Add(new LightRecord { name=light.name, enabled=light.enabled,
                    intensity=light.intensity, color=light.color, forward=light.transform.forward,
                    shadows=light.shadows.ToString(), cullingMask=light.cullingMask });
            var samples=new Color[Directions.Length]; expected.Evaluate(Directions,samples);
            var report=new Report { complete=complete, unity=Application.unityVersion, build=Application.buildGUID,
                colorSpace=QualitySettings.activeColorSpace.ToString(), startedUnityRealtime=started,
                pipeline=urp!=null?urp.name:"not_URP", additionalLightMode=urp!=null?urp.additionalLightsRenderingMode.ToString():"unknown",
                expectedCoefficients=Coefficients(expected), expectedShaderVectors=packed,
                expectedAmbientInUpDownRightLeftForwardBack=samples, diffuseFloorLinear=diffuseFloorLinear,
                upperBounceLinear=upperBounceLinear, upperBounceStrength=upperBounceStrength,
                directionalLights=directional.ToArray(), snapshots=snapshots.ToArray() };
            File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));
        }

        private static string Arg(string key) { var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key); return i>=0&&i+1<args.Length?args[i+1]:null; }
        private static bool Finite(Vector3 v) => !(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z));
        private static string TextureName(Material m,string property) { var texture=m.GetTexture(property); return texture!=null?texture.name:"none"; }
        private static float ProbeError(SphericalHarmonicsL2 a,SphericalHarmonicsL2 b) { float error=0; for(int c=0;c<3;c++)for(int i=0;i<9;i++)error=Mathf.Max(error,Mathf.Abs(a[c,i]-b[c,i])); return error; }
        public static float[] Coefficients(SphericalHarmonicsL2 sh) { var result=new float[27]; for(int c=0;c<3;c++)for(int i=0;i<9;i++)result[c*9+i]=sh[c,i]; return result; }

        [Serializable] private sealed class Report
        {
            public string kind="actual_runtime_environment_and_baked_normal_diagnostics_not_visual_or_performance_acceptance";
            public string boundary="Optional CPU BakeMesh snapshots observe current poses without advancing or altering simulation. They add overhead and are disabled in benchmark mode. SH/property-block readback is CPU-side data, not pixel proof. rendererSlot refers only to the actual Unity renderer/submesh index; actual material names are retained. No palette index or material role is inferred from array order. Geometric normal dots include original winding and transform determinant; region labels are normalized-height bins, not anatomical segmentation. Compare frame/time with the real video.";
            public string unity,build,colorSpace,pipeline,additionalLightMode;
            public bool complete; public double startedUnityRealtime;
            public Vector3 diffuseFloorLinear,upperBounceLinear; public float upperBounceStrength;
            public float[] expectedCoefficients; public Vector4[] expectedShaderVectors;
            public Color[] expectedAmbientInUpDownRightLeftForwardBack;
            public LightRecord[] directionalLights; public Snapshot[] snapshots;
        }
        [Serializable] private sealed class Snapshot { public double elapsedSinceEnvironmentEnable,unityRealtime; public int unityFrame,globalReapplications; public string ambientMode; public float globalReadbackError; public float[] actualGlobalCoefficients; public ActorRecord[] actors; }
        [Serializable] private sealed class ActorRecord
        {
            public string name,mesh,lightProbeUsage,shadowCasting; public int vertexCount,subMeshCount,nonFiniteNormals,zeroNormals;
            public bool receiveShadows; public uint renderingLayerMask; public Bounds bounds;
            public float transformDeterminant,minimumNormalLength,maximumNormalLength,maximumShaderVectorReadbackError;
            public Vector4[] suppliedShaderVectors; public MaterialRecord[] materials; public RegionRecord[] regions; public float[] shapeWeights;
        }
        [Serializable] private sealed class MaterialRecord { public int rendererSlot; public string name,shader,baseMap,normalMap,occlusionMap; public Vector4 baseColor,emission; public float metallic,smoothness; }
        [Serializable] private sealed class RegionRecord { public int rendererSlot,triangles,degenerateTriangles,opposedGeometricNormals,nearlyPerpendicularNormals,geometricFacesTowardCamera; public string actualMaterialName,region; public float minimumGeometricNormalDot,meanGeometricNormalDot; }
        [Serializable] private sealed class LightRecord { public string name,shadows; public bool enabled; public float intensity; public Color color; public Vector3 forward; public int cullingMask; }
    }
}
