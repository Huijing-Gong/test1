using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Native skeleton bridge. All geometry is exported in Unity mesh-local space;
// no vendor model, shared atlas, root capsule, combat script or terrain is edited.
public static class SilverSentinelRemodelInstaller
{
    public const string Folder = "Assets/Art/Enemies/AdventureTrio/SilverSentinel";
    public const string Output = "LocalDocs/Enemies/SilverSentinelRemodel-20261021";
    public const string PrefabPath = "Assets/Art/Enemies/AdventureTrio/Generated/BishopKnight.prefab";
    public const string SourceJson = Output + "/Source/knight-export.json";
    public const string ModelJson = Output + "/Model/knight-remodel.json";
    public const string ShaderName = "FoldedRealm/SilverSentinelToon";
    public const string OriginalSpearMaterial = "Assets/Art/Enemies/AdventureTrio/Styled/Knight/KnightSpear.mat";
    public const string OriginalShieldMaterial = "Assets/Art/Enemies/AdventureTrio/Styled/Knight/KnightShield.mat";
    const string NativePrefab = "Assets/RPGMonsterWave03PBR/Prefab/Characters/BishopKnightPBR.prefab";
    public const float NeckCompression = .50f;
    public const float HeadCompression = .85f;
    const string NativeFolder = "Assets/Art/Enemies/AdventureTrio/Generated/BishopKnight";
    static readonly string[] MaterialNames = { "Silver", "Indigo", "Gold", "Dark", "Eye" };
    static readonly string[] NeckNames = { "bishopKnight_neck02", "bishopKnight_neck03" };

    [Serializable] public sealed class MeshDocument
    {
        public int version = 1;
        public string coordinateSystem = "Unity mesh-local metres; matrices row-major Unity; no handedness conversion";
        public string sourcePrefab, animatorPath, sourceController;
        public float neckCompression = NeckCompression;
        public Part[] parts;
        public Bone[] skeleton;
    }
    [Serializable] public sealed class Part
    {
        public string name, sourceRendererPath, sourceMeshPath;
        public bool skinned;
        public float[] vertices, normals, uv, vertexAO, sourceColors, sourceEmission;
        public int[] triangles, boneIndices, paletteCells;
        public float[] boneWeights, bindposes, boneRestPivots, meshToRoot, rootToMesh;
        public string[] boneNames, bonePaths;
        public Submesh[] submeshes;
    }
    [Serializable] public sealed class Submesh { public string material; public int[] triangles; }
    [Serializable] public sealed class Bone
    {
        public string name, path, parentPath;
        public float[] localPosition, localRotation, localScale, rootPosition;
    }

    [MenuItem("Tools/Silver Sentinel/1 Export Native Geometry")]
    public static void Export()
    {
        Guard();
        Directory.CreateDirectory(Path.GetDirectoryName(SourceJson));
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var atlas = Decode("Assets/RPGMonsterWave03PBR/Texture/DefaultPBR/Albedo.png");
        var glow = Decode("Assets/RPGMonsterWave03PBR/Texture/DefaultPBR/Emission.png");
        try
        {
            var animator = root.GetComponent<AdventureCreatureAI>().animator;
            var renderers = animator.GetComponentsInChildren<Renderer>(true);
            Need(renderers.Length == 3, "Exactly three native Knight renderers");
            Need(renderers.All(r=>!AssetDatabase.GetAssetPath(MeshOf(r)).StartsWith(Folder+"/",StringComparison.Ordinal)),"Do not overwrite the native geometry source export after installing the remodel");
            var document = new MeshDocument {
                sourcePrefab = PrefabPath, animatorPath = PathOf(animator.transform, root.transform),
                sourceController = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController),
                parts = renderers.Select(r => ExportPart(r, root.transform, atlas, glow)).ToArray(),
                skeleton = animator.GetComponentsInChildren<Transform>(true).Select(t => new Bone {
                    name = t.name, path = PathOf(t, root.transform), parentPath = t.parent ? PathOf(t.parent, root.transform) : "",
                    localPosition = V(t.localPosition), localRotation = new[] { t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w },
                    localScale = V(t.localScale), rootPosition = V(root.transform.InverseTransformPoint(t.position)) }).ToArray()
            };
            File.WriteAllText(SourceJson, JsonUtility.ToJson(document, true));
            File.WriteAllText(Output + "/Source/coordinate-contract.txt",
                "All vertices/normals/UV0/bone weights use original Unity mesh-local coordinates.\n" +
                "Matrices are row-major: x'=m0*x+m1*y+m2*z+m3. meshToRoot maps the authored prefab pose into the physical unit root.\n" +
                "Skinned bindposes and vertex indices refer to the unchanged ordered boneNames/bonePaths.\n" +
                "boneRestPivots are bindposes[i].inverse * origin in mesh-local coordinates.\n" +
                "Blender output must use the same coordinate frame and ordered weights. Do not apply FBX axis conversion.\n" +
                "Output parts require submeshes labels Silver/Indigo/Gold/Dark/Eye, optional vertexAO per vertex.\n" +
                "Skeleton localPosition records the authored native pose; neck02/03 position curves and rest offsets are compressed by .50; head by .85 during installation.\n");
            Debug.Log("SILVER_SENTINEL EXPORT PASS: " + SourceJson + " | " + string.Join("; ", document.parts.Select(p => p.name + " " + p.vertices.Length / 3 + " verts")));
        }
        finally { Object.DestroyImmediate(atlas); Object.DestroyImmediate(glow); PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void ValidateActor(GameObject actor)
    {
        Need(actor && actor.GetComponent<BishopKnightAI>(), "Remodeled actor has its real territorial Knight AI");
        var ai = actor.GetComponent<BishopKnightAI>(); var animator = ai.animator;
        Need(animator && animator.runtimeAnimatorController, "Native animator and controller exist");
        Need(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) == Folder + "/Animations/Native.controller", "Independent native controller clone is assigned");
        Need(!animator.applyRootMotion && actor.transform.localScale == Vector3.one, "Physical root remains unit scale with no animation root motion");
        var capsule = actor.GetComponent<CapsuleCollider>();
        Need(capsule && Mathf.Abs(capsule.height-3.15f)<.001f && Mathf.Abs(capsule.radius-.8f)<.001f && capsule.center==new Vector3(0,1.575f,0), "Original miniboss physical dimensions retained");
        Need(actor.GetComponent<Health>().MaxHealth==240 && ai.IsTerritoryBoss && ai.noticeRange==9 && ai.leashRange==11 && ai.walkSpeed==1.6f, "Original miniboss health, perception and movement retained");
        var renderers=animator.GetComponentsInChildren<Renderer>(true);
        Need(renderers.Length==3 && renderers.Count(r=>r is SkinnedMeshRenderer)==1,"Exactly the original three native renderers are retained");
        var native=AssetDatabase.LoadAssetAtPath<GameObject>(NativePrefab);
        Need(native,"Original vendor Knight remains available");
        var nativeAnimator=native.GetComponentInChildren<Animator>(true);
        Need(nativeAnimator && animator.avatar==nativeAnimator.avatar,"Original native avatar remains assigned");
        Need(animator.GetComponentsInChildren<Transform>(true).Select(t=>PathOf(t,animator.transform)).OrderBy(p=>p)
            .SequenceEqual(nativeAnimator.GetComponentsInChildren<Transform>(true).Select(t=>PathOf(t,nativeAnimator.transform)).OrderBy(p=>p)),"Complete original transform hierarchy is unchanged; details are geometry, not added rig nodes");
        var sourceRenderers=native.GetComponentsInChildren<Renderer>(true);
        var used=new HashSet<string>();
        foreach(var renderer in renderers)
        {
            Need(sourceRenderers.Any(r=>r.name==renderer.name && r.GetType()==renderer.GetType()),"Native renderer name/type retained: "+renderer.name);
            if(renderer.name=="Bishop_Spear") { ValidateOriginalSpear(actor); continue; }
            if(renderer.name=="Bishop_Shield") { ValidateOriginalShield(actor); continue; }
            var mesh=MeshOf(renderer); Need(mesh && AssetDatabase.GetAssetPath(mesh)==Folder+"/Meshes/"+renderer.name+".asset","Exact independent remodeled mesh: "+renderer.name);
            Need(mesh.vertexCount>=100 && mesh.vertexCount<=150000 && mesh.subMeshCount==renderer.sharedMaterials.Length && mesh.subMeshCount>0,"Finite-sized mesh with exact material slots: "+renderer.name);
            Need(mesh.vertices.All(Finite) && mesh.normals.Length==mesh.vertexCount && mesh.normals.All(Finite) && mesh.uv.Length==mesh.vertexCount && mesh.uv.All(v=>Finite(v.x)&&Finite(v.y)),"Finite complete mesh buffers: "+renderer.name);
            Need(mesh.colors32.Length==mesh.vertexCount && mesh.colors32.All(c=>c.r>=20&&c.r==c.g&&c.g==c.b&&c.a==255),"Per-vertex cavity AO is bounded, opaque and neutral: "+renderer.name);
            Need(mesh.bounds.size.sqrMagnitude>.0001f && Finite(mesh.bounds.center)&&Finite(mesh.bounds.size),"Valid local bounds: "+renderer.name);
            for(int s=0;s<mesh.subMeshCount;s++)
            {
                var indices=mesh.GetTriangles(s); Need(indices.Length>0 && indices.Length%3==0 && indices.All(i=>i>=0&&i<mesh.vertexCount),"Valid material triangle partition: "+renderer.name);
                var mat=renderer.sharedMaterials[s]; Need(mat && MaterialNames.Contains(mat.name) && AssetDatabase.GetAssetPath(mat)==Folder+"/Materials/"+mat.name+".mat" && mat.shader && mat.shader.name==ShaderName,"Exact approved independent material/shader: "+renderer.name);
                used.Add(mat.name);
            }
            if(renderer is SkinnedMeshRenderer skin)
            {
                var source=sourceRenderers.OfType<SkinnedMeshRenderer>().Single();
                Need(skin.bones.Length==source.bones.Length && skin.bones.Select(b=>b?b.name:"").SequenceEqual(source.bones.Select(b=>b?b.name:"")),"All original ordered skeleton bone names remain assigned");
                Need(skin.bones.Select(b=>PathOf(b,animator.transform)).SequenceEqual(source.bones.Select(b=>PathOf(b,nativeAnimator.transform))) && skin.rootBone && source.rootBone && skin.rootBone.name==source.rootBone.name,"Original bone paths, parents and skin root are retained");
                Need(mesh.bindposes.Length==source.sharedMesh.bindposes.Length && mesh.bindposes.Zip(source.sharedMesh.bindposes,(a,b)=>MatrixNear(a,b)).All(b=>b),"Original bind-pose matrices retained exactly");
                var weights=mesh.boneWeights; Need(weights.Length==mesh.vertexCount,"Every remodeled vertex is skinned");
                foreach(var w in weights) Need(WeightValid(w,skin.bones.Length),"Normalized original-bone skin weights");
                Need(mesh.vertexCount>source.sharedMesh.vertexCount,"Body contains real remodeled bevel/trim geometry, not only a recolor");
            }
        }
        Need(used.SetEquals(MaterialNames),"The full five-material silver/indigo/gold/dark/eye palette is used");
        var shield=renderers.Single(r=>r.name.IndexOf("Shield",StringComparison.OrdinalIgnoreCase)>=0);
        var originalShield=sourceRenderers.Single(r=>r.name==shield.name);
        Need(MeshOf(shield)==MeshOf(originalShield),"Shield uses exact original native mesh");
        var spear=renderers.Single(r=>r.name.IndexOf("Spear",StringComparison.OrdinalIgnoreCase)>=0);
        var originalSpear=sourceRenderers.Single(r=>r.name==spear.name);
        var originalBounds=MeshOf(originalSpear).bounds; var actualBounds=MeshOf(spear).bounds;
        Need((originalBounds.min-actualBounds.min).magnitude<.003f && (originalBounds.max-actualBounds.max).magnitude<.003f,"Native spear blade extents and contact geometry retained");
        Need(ai.weapon==spear.transform && EndpointInBounds(ai.weaponBaseLocal,actualBounds) && EndpointInBounds(ai.weaponTipLocal,actualBounds),"Actual animated spear remains the combat contact source");
        ValidateClips(animator.runtimeAnimatorController);
        var style=actor.GetComponent<AdventureCreatureStyle>(); Need(style && style.species==AdventureCreatureStyle.Species.Knight && style.targetRenderers.Length==3 && renderers.All(style.targetRenderers.Contains),"Existing presentation component uses only the original three renderers");
        Bounds body=BodyBounds(animator.gameObject,actor.transform);
        Need(Mathf.Abs(body.size.y-3.15f)<.06f && Mathf.Abs(body.min.y)<.025f && new Vector2(body.center.x,body.center.z).magnitude<.025f,"Remodeled real body is 3.15 m, centred and grounded on unit physical root");
    }

    // User keeps the precise pre-remodel weapon, including its textured material.
    // Do not replace it with the unused separate Weapons/ FBX asset of similar name.
    public static void ValidateOriginalSpear(GameObject actor)
    {
        var ai=actor.GetComponent<BishopKnightAI>(); Need(ai&&ai.animator,"Original spear belongs to the actual native Knight");
        var renderer=ai.animator.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Bishop_Spear");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(NativePrefab).GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Bishop_Spear");
        Need(MeshOf(renderer)==MeshOf(source),"Original native character-FBX spear mesh reference is restored exactly");
        var material=AssetDatabase.LoadAssetAtPath<Material>(OriginalSpearMaterial);
        Need(material&&renderer.sharedMaterials.Length==1&&renderer.sharedMaterials[0]==material,"Original pre-remodel textured KnightSpear material is retained exactly");
        Need(material.shader&&material.shader.name=="FoldedRealm/AdventureCreatureToon","Original spear keeps its existing shader and texture appearance");
        Need(ai.weapon==renderer.transform,"Original animated spear socket remains the actual damage-contact source");
    }

    public static void ValidateOriginalShield(GameObject actor)
    {
        var renderer=actor.GetComponent<BishopKnightAI>().animator.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Bishop_Shield");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(NativePrefab).GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Bishop_Shield");
        Need(MeshOf(renderer)==MeshOf(source),"Original shield mesh reference retained exactly");
        var material=AssetDatabase.LoadAssetAtPath<Material>(OriginalShieldMaterial);
        Need(material&&renderer.sharedMaterials.Length==1&&renderer.sharedMaterial==material,"Original pre-remodel shield material retained exactly");
        Need(renderer.transform.localPosition==source.transform.localPosition && renderer.transform.localRotation==source.transform.localRotation && renderer.transform.localScale==source.transform.localScale,"Original shield grip transform retained exactly");
    }

    static void RestoreOriginalShield(GameObject actor)
    {
        var renderer=actor.GetComponent<BishopKnightAI>().animator.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Bishop_Shield");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(NativePrefab).GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Bishop_Shield");
        var filter=renderer.GetComponent<MeshFilter>();filter.sharedMesh=MeshOf(source);
        renderer.sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(OriginalShieldMaterial)};
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        ValidateOriginalShield(actor);
    }

    static void RestoreOriginalSpear(GameObject actor)
    {
        var ai=actor.GetComponent<BishopKnightAI>();
        var renderer=ai.animator.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Bishop_Spear");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(NativePrefab).GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Bishop_Spear");
        var filter=renderer.GetComponent<MeshFilter>(); filter.sharedMesh=MeshOf(source);
        renderer.sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(OriginalSpearMaterial)};
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        ValidateOriginalSpear(actor);
    }

    [MenuItem("Tools/Silver Sentinel/2 Install Blender Remodel")]
    public static void Install()
    {
        Guard(); for(int i=0;i<SceneManager.sceneCount;i++) Need(!SceneManager.GetSceneAt(i).isDirty,"Save scenes before isolated remodel authoring");
        Need(File.Exists(SourceJson)&&File.Exists(ModelJson),"Both native export and Blender-produced remodel JSON exist");
        var source=JsonUtility.FromJson<MeshDocument>(File.ReadAllText(SourceJson));
        var result=JsonUtility.FromJson<MeshDocument>(File.ReadAllText(ModelJson));
        Need(source!=null && result!=null && source.version==1 && result.version==1 && result.parts?.Length==2,"Versioned Blender armor/shield document, with original spear explicitly excluded from remodeling");
        Need(result.parts.Select(p=>p.name).OrderBy(n=>n).SequenceEqual(source.parts.Where(p=>p.name!="Bishop_Spear").Select(p=>p.name).OrderBy(n=>n)),"Blender returns exactly body and shield; weapon stays the original source asset");
        var scene=EditorSceneManager.OpenScene(AdventureTrioInstaller.ScenePath);
        var all=Object.FindObjectsByType<Health>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(h=>h.gameObject.scene==scene&&h.Team==HealthTeam.Enemy).ToArray();
        var knight=all.Single(h=>h.GetComponent<BishopKnightAI>());
        Need(all.Length==20,"Exactly the existing twenty authored encounters are in scope");
        var protectedActors=all.Where(h=>h!=knight).Select(h=>new ActorSnapshot(h)).ToArray();
        var sourceHashes=CaptureProtectedHashes(); string knightAI=EditorJsonUtility.ToJson(knight.GetComponent<BishopKnightAI>());
        Vector3 knightPosition=knight.transform.position; Quaternion knightRotation=knight.transform.rotation;
        string prefabGuid=AssetDatabase.AssetPathToGUID(PrefabPath);
        EnsureFolder(Folder+"/Meshes"); EnsureFolder(Folder+"/Materials"); EnsureFolder(Folder+"/Animations");
        try
        {
            var shader=Shader.Find(ShaderName); Need(shader&&shader.isSupported,"Approved Silver Sentinel toon shader compiles and is supported");
            var materials=MaterialNames.ToDictionary(n=>n,n=>MakeMaterial(n,shader));
            var clips=CloneClips(); var controller=CloneController(clips);
            var root=PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var ai=root.GetComponent<BishopKnightAI>(); var animator=ai.animator;
                var renderers=animator.GetComponentsInChildren<Renderer>(true); Need(renderers.Length==3,"Original renderer count before remodel");
                foreach(var part in result.parts)
                {
                    if(part.name=="Bishop_Shield")continue; // Native shield requested; archived Blender shield is not applied.
                    var original=source.parts.Single(p=>p.name==part.name);
                    Need(part.sourceRendererPath==original.sourceRendererPath && part.skinned==original.skinned,"Exact native part identity and coordinate frame");
                    var renderer=renderers.Single(r=>PathOf(r.transform,root.transform)==part.sourceRendererPath);
                    var mesh=ImportMesh(part,original);
                    if(renderer is SkinnedMeshRenderer skin) { skin.sharedMesh=mesh; skin.localBounds=mesh.bounds; skin.updateWhenOffscreen=true; }
                    else renderer.GetComponent<MeshFilter>().sharedMesh=mesh;
                    renderer.sharedMaterials=part.submeshes.Select(s=>materials[s.material]).ToArray();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    if(renderer.GetComponent<MeshFilter>()) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.GetComponent<MeshFilter>());
                }
                RestoreOriginalSpear(root);
                RestoreOriginalShield(root);
                animator.runtimeAnimatorController=controller; animator.applyRootMotion=false;
                foreach(var bone in source.skeleton)
                {
                    float factor=PositionFactor(bone.name); if(factor==1)continue;
                    var transform=root.transform.Find(bone.path); Need(transform,"Original compressed neck/head bone remains: "+bone.path);
                    transform.localPosition=new Vector3(bone.localPosition[0],bone.localPosition[1],bone.localPosition[2])*factor;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
                }
                Fit(animator.gameObject,root.transform,clips["IdleNormal"]);
                var style=root.GetComponent<AdventureCreatureStyle>(); style.targetRenderers=renderers;
                EditorUtility.SetDirty(style); EditorUtility.SetDirty(animator);
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                ValidateActor(root);
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath,out bool saved); Need(saved,"Existing Knight prefab saved with preserved GUID");
                Need(AssetDatabase.AssetPathToGUID(PrefabPath)==prefabGuid,"Original generated prefab GUID unchanged");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets(); Physics.SyncTransforms();
            // Remove only our now-unused generated replacement, never a vendor mesh.
            string obsoleteSpear=Folder+"/Meshes/Bishop_Spear.asset";
            if(AssetDatabase.LoadAssetAtPath<Mesh>(obsoleteSpear))
                Need(AssetDatabase.DeleteAsset(obsoleteSpear),"Removed own unused replacement spear mesh; original source remains untouched");
            Need(knight.transform.position==knightPosition && knight.transform.rotation==knightRotation,"Knight home and facing unchanged");
            Need(EditorJsonUtility.ToJson(knight.GetComponent<BishopKnightAI>())==knightAI,"All AI and actual weapon-contact settings unchanged");
            foreach(var snapshot in protectedActors)snapshot.Verify(); VerifyProtectedHashes(sourceHashes);
            ValidateActor(knight.gameObject); AdventureTrioInstaller.ValidateScene(scene);
            EditorSceneManager.MarkSceneDirty(scene); Need(EditorSceneManager.SaveScene(scene),"Validated existing scene saved");
            Directory.CreateDirectory(Output);
            File.WriteAllLines(Output+"/protected-source-sha256.txt",sourceHashes.OrderBy(p=>p.Key).Select(p=>p.Value+"  "+p.Key));
            File.WriteAllLines(Output+"/generated-manifest.txt",AssetDatabase.GetAllAssetPaths().Where(p=>p==Folder||p.StartsWith(Folder+"/",StringComparison.Ordinal)).OrderBy(p=>p));
            File.WriteAllLines(Output+"/preserved-19-actors.txt",protectedActors.Select(p=>p.Description));
            File.WriteAllText(Output+"/geometry-import-audit.txt",string.Join("\n",result.parts.Select(p=>p.name+" vertices="+p.vertices.Length/3+" triangles="+p.submeshes.Sum(s=>s.triangles.Length)/3+" materialSlots="+string.Join(",",p.submeshes.Select(s=>s.material))))+"\nBishop_Spear: exact original character-FBX mesh reference and pre-remodel KnightSpear material restored. No replacement weapon mesh retained.\nNative neck02/03 position curves x0.50; head position curves x0.85. All rotation curves, event timings and clip lengths preserved.\nNative spear bounds/contact endpoints remain unchanged.\n");
            Debug.Log("SILVER_SENTINEL INSTALL PASS: beveled armor/shield remodel with exact original spear mesh/material; native rig/actions/AI/contact source preserved; five independent armor materials; 19 other actors and protected source hashes unchanged.");
        }
        catch { EditorSceneManager.OpenScene(AdventureTrioInstaller.ScenePath,OpenSceneMode.Single); throw; }
    }

    static Mesh ImportMesh(Part part,Part source)
    {
        Need(part.vertices!=null&&part.vertices.Length%3==0,"XYZ vertex buffer exists"); int count=part.vertices.Length/3;
        Need(count>=100&&count<=150000&&part.vertices.All(Finite),"Finite bounded remodeled vertex buffer");
        Need(part.normals?.Length==count*3&&part.normals.All(Finite)&&part.uv?.Length==count*2&&part.uv.All(Finite),"Complete finite normals and UV0 from Blender");
        Need(part.vertexAO?.Length==count&&part.vertexAO.All(a=>Finite(a)&&a>=.08f&&a<=1),"Bounded baked per-vertex cavity shading");
        Need(part.submeshes!=null&&part.submeshes.Length>0&&part.submeshes.Select(s=>s.material).Distinct().Count()==part.submeshes.Length,"Unique material partitions");
        Need(part.submeshes.All(s=>MaterialNames.Contains(s.material)&&s.triangles!=null&&s.triangles.Length>0&&s.triangles.Length%3==0&&s.triangles.All(i=>i>=0&&i<count)),"Only exact approved material labels and finite index ranges");
        var fresh=new Mesh {name=part.name+" Silver Sentinel beveled remodel",indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
        fresh.vertices=Enumerable.Range(0,count).Select(i=>new Vector3(part.vertices[3*i],part.vertices[3*i+1],part.vertices[3*i+2])).ToArray();
        fresh.normals=Enumerable.Range(0,count).Select(i=>new Vector3(part.normals[3*i],part.normals[3*i+1],part.normals[3*i+2]).normalized).ToArray();
        fresh.uv=Enumerable.Range(0,count).Select(i=>new Vector2(part.uv[2*i],part.uv[2*i+1])).ToArray();
        fresh.colors32=part.vertexAO.Select(a=>{byte b=(byte)Mathf.RoundToInt(a*255);return new Color32(b,b,b,255);}).ToArray();
        if(part.skinned)
        {
            Need(part.boneNames.SequenceEqual(source.boneNames)&&part.bonePaths.SequenceEqual(source.bonePaths),"Exact original ordered bone identity");
            Need(part.bindposes.SequenceEqual(source.bindposes),"Original bindposes returned unchanged");
            Need(part.boneIndices?.Length==4*count&&part.boneWeights?.Length==4*count&&part.boneWeights.All(Finite),"Four original-bone influences per remodeled vertex");
            fresh.boneWeights=Enumerable.Range(0,count).Select(i=>new BoneWeight {boneIndex0=part.boneIndices[4*i],boneIndex1=part.boneIndices[4*i+1],boneIndex2=part.boneIndices[4*i+2],boneIndex3=part.boneIndices[4*i+3],weight0=part.boneWeights[4*i],weight1=part.boneWeights[4*i+1],weight2=part.boneWeights[4*i+2],weight3=part.boneWeights[4*i+3]}).ToArray();
            Need(fresh.boneWeights.All(w=>WeightValid(w,source.boneNames.Length)),"Normalized in-range original-rig weights");
            fresh.bindposes=Enumerable.Range(0,source.bindposes.Length/16).Select(i=>Matrix(source.bindposes,i*16)).ToArray();
        }
        fresh.subMeshCount=part.submeshes.Length;
        for(int s=0;s<part.submeshes.Length;s++)fresh.SetTriangles(part.submeshes[s].triangles,s,false);
        fresh.RecalculateBounds(); fresh.RecalculateTangents();
        string path=Folder+"/Meshes/"+part.name+".asset"; var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh){EditorUtility.CopySerialized(fresh,mesh);Object.DestroyImmediate(fresh);EditorUtility.SetDirty(mesh);}else{mesh=fresh;AssetDatabase.CreateAsset(mesh,path);}
        return mesh;
    }

    [MenuItem("Tools/Silver Sentinel/3 Refresh Materials Only")]
    public static void RefreshMaterialsOnly()
    {
        Guard();
        var protectedPaths=Directory.GetFiles(Folder+"/Meshes","*",SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Folder+"/Animations","*",SearchOption.AllDirectories))
            .Concat(new[]{"Assets/Art/Enemies/AdventureTrio/Generated/BishopKnight.prefab",OriginalSpearMaterial});
        var before=protectedPaths.ToDictionary(p=>p,Hash);
        var shader=Shader.Find(ShaderName);Need(shader&&shader.isSupported,"Material refresh shader supported");
        foreach(var name in MaterialNames)MakeMaterial(name,shader);
        AssetDatabase.SaveAssets();
        foreach(var entry in before)Need(Hash(entry.Key)==entry.Value,"Material-only refresh preserves "+entry.Key);
        Debug.Log("SILVER_SENTINEL MATERIAL PASS: palette/satin response only; meshes, rig, clips, prefab and original spear unchanged.");
    }

    public static void RefreshAppearanceWithOriginalShield()
    {
        RefreshMaterialsOnly();
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            RestoreOriginalShield(root);
            ValidateActor(root);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("SILVER_SENTINEL APPEARANCE PASS: satin materials and exact original shield; body geometry and original spear preserved.");
    }

    static Material MakeMaterial(string name,Shader shader)
    {
        string path=Folder+"/Materials/"+name+".mat"; var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);} mat.shader=shader;mat.name=name;
        bool silver=name=="Silver",gold=name=="Gold",eye=name=="Eye",indigo=name=="Indigo";
        // CombatFeedback restores one shared _BaseColor per Renderer. Keep
        // that tint neutral; the five submesh palettes must remain per-material.
        mat.SetTexture("_BaseMap",Texture2D.whiteTexture);mat.SetColor("_BaseColor",Color.white);
        mat.SetColor("_ArmorColor",Hex(silver?0xD4D8DD:gold?0xDBAE58:eye?0x7AE5F2:indigo?0x172B4B:0x101923));
        mat.SetTexture("_EmissionMap",eye?Texture2D.whiteTexture:Texture2D.blackTexture);mat.SetColor("_EmissionColor",Hex(0x44DDDF));
        mat.SetTexture("_TechMask",Texture2D.blackTexture);mat.SetTexture("_OcclusionMap",Texture2D.whiteTexture);
        mat.SetColor("_ShadowColor",new Color(.40f,.45f,.54f,1));mat.SetColor("_DeepShadowColor",new Color(.16f,.20f,.29f,1));
        mat.SetColor("_RimColor",Hex(gold?0xF4D79A:0xBCCADB));mat.SetColor("_SpecularColor",gold?Hex(0xFFE7B5):Hex(0xF1F3F7));
        Set(mat,"_ShadeThreshold",.32f);Set(mat,"_DeepShadeThreshold",-.24f);Set(mat,"_ShadeSoftness",.24f);Set(mat,"_FormStrength",.62f);
        Set(mat,"_AmbientStrength",.32f);Set(mat,"_Metallic",silver?.65f:gold?.78f:indigo?.12f:0);Set(mat,"_Smoothness",silver?.52f:gold?.62f:indigo?.32f:.20f);
        Set(mat,"_SpecularStrength",silver?.78f:gold?.85f:indigo?.22f:.06f);Set(mat,"_SpecularAA",1);Set(mat,"_LightColorPreservation",.9f);Set(mat,"_ShadowLightSaturation",.1f);
        Set(mat,"_SatinReflection",silver?.38f:gold?.30f:indigo?.12f:.02f);
        Set(mat,"_AdditionalLightStrength",.48f);Set(mat,"_GrazingShade",.10f);Set(mat,"_RimStrength",eye?0:silver?.024f:gold?.018f:.006f);Set(mat,"_RimPower",4.5f);
        Set(mat,"_EmissionStrength",eye?1.1f:0);Set(mat,"_StyleLive",1);Set(mat,"_StyleClock",0);Set(mat,"_TechPulse",0);Set(mat,"_TechStrength",0);Set(mat,"_TechWeapon",0);
        Set(mat,"_VertexAoStrength",.72f);Set(mat,"_BumpScale",0);Set(mat,"_OcclusionStrength",0);Set(mat,"_Heat",0);Set(mat,"_ReceiveShadows",1);Set(mat,"_Cull",2);
        EditorUtility.SetDirty(mat);return mat;
    }
    static void Set(Material mat,string property,float value){Need(mat.HasProperty(property),"Remodel shader property exists: "+property);mat.SetFloat(property,value);}
    static Color Hex(int rgb)=>new Color(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,1);

    static Dictionary<string,AnimationClip> CloneClips()
    {
        var result=new Dictionary<string,AnimationClip>();
        foreach(string sourcePath in AssetDatabase.FindAssets("t:AnimationClip",new[]{NativeFolder}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p))
        {
            var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);Need(source,"Native animation clip exists");
            string path=Folder+"/Animations/"+Path.GetFileName(sourcePath);var clone=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(!clone){clone=Object.Instantiate(source);AssetDatabase.CreateAsset(clone,path);}else EditorUtility.CopySerialized(source,clone);
            clone.name=source.name;
            foreach(var binding in AnimationUtility.GetCurveBindings(source))
            {
                float factor=PositionFactor(binding.path.Split('/').Last());
                if(factor==1||!binding.propertyName.StartsWith("m_LocalPosition.",StringComparison.Ordinal))continue;
                var curve=AnimationUtility.GetEditorCurve(source,binding);var keys=curve.keys;
                for(int i=0;i<keys.Length;i++){var key=keys[i];key.value*=factor;key.inTangent*=factor;key.outTangent*=factor;keys[i]=key;}
                curve.keys=keys;AnimationUtility.SetEditorCurve(clone,binding,curve);
            }
            EditorUtility.SetDirty(clone);result.Add(source.name,clone);
        }
        Need(result.Count==18&&result.ContainsKey("IdleNormal")&&result.ContainsKey("Attack01")&&result.ContainsKey("Attack02"),"All eighteen native Knight actions cloned");return result;
    }
    static AnimatorController CloneController(Dictionary<string,AnimationClip> clips)
    {
        string path=Folder+"/Animations/Native.controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        // Copy the complete serialized controller including state subassets only
        // once; a rerun simply reassigns the original named motions to new clones.
        if(!controller){Need(AssetDatabase.CopyAsset(NativeFolder+"/Native.controller",path),"Copy complete original native controller");controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);}
        foreach(var layer in controller.layers)foreach(var state in States(layer.stateMachine))
        {Need(state.motion is AnimationClip,"Native controller contains only directly named animation clips");Need(clips.TryGetValue(state.motion.name,out var clip),"All native controller states map to cloned actions");state.motion=clip;EditorUtility.SetDirty(state);}
        EditorUtility.SetDirty(controller);return controller;
    }
    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {foreach(var state in machine.states)yield return state.state;foreach(var child in machine.stateMachines)foreach(var state in States(child.stateMachine))yield return state;}
    static float PositionFactor(string bone)=>NeckNames.Contains(bone)?NeckCompression:bone=="bishopKnight_head"?HeadCompression:1;
    static void ValidateClips(RuntimeAnimatorController controller)
    {
        var clips=controller.animationClips.Distinct().ToArray();Need(clips.Length==18,"All eighteen native actions remain playable");
        foreach(var clip in clips)
        {
            Need(AssetDatabase.GetAssetPath(clip)==Folder+"/Animations/"+clip.name+".anim","Controller uses only independently cloned native actions");
            var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(NativeFolder+"/"+clip.name+".anim");
            Need(source&&Mathf.Abs(source.length-clip.length)<.0001f&&source.frameRate==clip.frameRate,"Original action duration and sample rate retained");
            var bindings=AnimationUtility.GetCurveBindings(source);var actual=AnimationUtility.GetCurveBindings(clip);
            Need(bindings.Select(BindingKey).OrderBy(k=>k).SequenceEqual(actual.Select(BindingKey).OrderBy(k=>k)),"Original animation binding paths/properties retained");
            foreach(var binding in bindings)
            {
                var a=AnimationUtility.GetEditorCurve(source,binding);var b=AnimationUtility.GetEditorCurve(clip,binding);
                float factor=binding.propertyName.StartsWith("m_LocalPosition.",StringComparison.Ordinal)?PositionFactor(binding.path.Split('/').Last()):1;
                Need(a.length==b.length && a.preWrapMode==b.preWrapMode && a.postWrapMode==b.postWrapMode,"Curve key count and wrapping retained");
                var ak=a.keys;var bk=b.keys;
                for(int i=0;i<ak.Length;i++)Need(ak[i].time==bk[i].time&&Near(ak[i].value*factor,bk[i].value)&&Near(ak[i].inTangent*factor,bk[i].inTangent)&&Near(ak[i].outTangent*factor,bk[i].outTangent)&&ak[i].inWeight==bk[i].inWeight&&ak[i].outWeight==bk[i].outWeight&&ak[i].weightedMode==bk[i].weightedMode,"Only approved neck/head position values and tangents differ; all action timing/rotation/other keys remain exact");
            }
            Need(AnimationUtility.GetAnimationEvents(source).Select(EventKey).SequenceEqual(AnimationUtility.GetAnimationEvents(clip).Select(EventKey)),"Native animation events remain unchanged");
        }
    }
    static string BindingKey(EditorCurveBinding b)=>b.path+"|"+b.propertyName+"|"+b.type.FullName;
    static string EventKey(AnimationEvent e)=>e.time+"|"+e.functionName+"|"+e.stringParameter+"|"+e.floatParameter+"|"+e.intParameter+"|"+AssetDatabase.GetAssetPath(e.objectReferenceParameter);
    static bool Near(float a,float b)=>a==b||Mathf.Abs(a-b)<.00005f;

    static void Fit(GameObject visual,Transform physicalRoot,AnimationClip idle)
    {
        Sample(visual,idle);Bounds before=BodyBounds(visual,physicalRoot);Need(before.size.y>.1f,"Remodeled native body is measurable");
        visual.transform.localScale*=3.15f/before.size.y;Sample(visual,idle);Bounds body=BodyBounds(visual,physicalRoot);
        Vector3 delta=physicalRoot.TransformVector(new Vector3(-body.center.x,-body.min.y,-body.center.z));visual.transform.position+=delta;
        Physics.SyncTransforms();PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
        Need(Mathf.Abs(BodyBounds(visual,physicalRoot).size.y-3.15f)<.025f,"Remodeled body fitted once without accumulating scale");
    }
    static void Sample(GameObject visual,AnimationClip clip)
    {var t=visual.transform;Vector3 p=t.localPosition,s=t.localScale;Quaternion q=t.localRotation;clip.SampleAnimation(visual,0);t.localPosition=p;t.localRotation=q;t.localScale=s;}
    static Bounds BodyBounds(GameObject visual,Transform root)
    {
        bool found=false;Bounds bounds=default;var baked=new Mesh();
        try {foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {skin.BakeMesh(baked,true);foreach(var vertex in baked.vertices){Vector3 p=root.InverseTransformPoint(skin.transform.TransformPoint(vertex));if(found)bounds.Encapsulate(p);else{bounds=new Bounds(p,Vector3.zero);found=true;}}baked.Clear();}}
        finally{Object.DestroyImmediate(baked);}Need(found,"Actual remodeled skinned body exists");return bounds;
    }

    sealed class ActorSnapshot
    {
        readonly Health actor;readonly Vector3 position,scale;readonly Quaternion rotation;readonly int hp;readonly string appearance,prefab;
        public string Description=>actor.name+" | "+position.ToString("F5")+" | "+scale.ToString("F5")+" | HP "+hp+" | "+prefab;
        public ActorSnapshot(Health health){actor=health;position=health.transform.position;rotation=health.transform.rotation;scale=health.transform.localScale;hp=health.MaxHealth;appearance=Appearance(health.gameObject);prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(health.gameObject);}
        public void Verify(){Need(actor&&actor.transform.position==position&&actor.transform.rotation==rotation&&actor.transform.localScale==scale&&actor.MaxHealth==hp&&Appearance(actor.gameObject)==appearance&&PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(actor.gameObject)==prefab,"Other encounter source/appearance/placement/health preserved: "+(actor?actor.name:"missing actor"));}
    }
    static string Appearance(GameObject actor)=>string.Join(";",actor.GetComponentsInChildren<Renderer>(true).Select(r=>r.name+"|"+AssetDatabase.GetAssetPath(MeshOf(r))+"|"+string.Join(",",r.sharedMaterials.Select(AssetDatabase.GetAssetPath))));
    static Dictionary<string,string> CaptureProtectedHashes()
    {
        var roots=new[]{"Assets/RPGMonsterWave03PBR",NativeFolder,"Assets/Art/Enemies/AdventureTrio/Styled","Assets/Art/Enemies/DemonKingTrial"};
        return roots.Where(Directory.Exists).SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Distinct().ToDictionary(p=>p,Hash);
    }
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static void VerifyProtectedHashes(Dictionary<string,string> expected){foreach(var entry in expected)Need(File.Exists(entry.Key)&&Hash(entry.Key)==entry.Value,"Protected vendor/native animation/other species/Demon source bytes unchanged: "+entry.Key);}
    static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
    static Matrix4x4 Matrix(float[] a,int offset){var m=new Matrix4x4();for(int row=0;row<4;row++)for(int column=0;column<4;column++)m[row,column]=a[offset+4*row+column];return m;}
    static bool MatrixNear(Matrix4x4 a,Matrix4x4 b){for(int r=0;r<4;r++)for(int c=0;c<4;c++)if(!Near(a[r,c],b[r,c]))return false;return true;}
    static bool WeightValid(BoneWeight w,int count)=>w.boneIndex0>=0&&w.boneIndex0<count&&w.boneIndex1>=0&&w.boneIndex1<count&&w.boneIndex2>=0&&w.boneIndex2<count&&w.boneIndex3>=0&&w.boneIndex3<count&&w.weight0>=0&&w.weight1>=0&&w.weight2>=0&&w.weight3>=0&&Near(w.weight0+w.weight1+w.weight2+w.weight3,1);
    static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f)&&Mathf.Abs(f)<1000000;
    static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
    static bool EndpointInBounds(Vector3 point,Bounds bounds){bounds.Expand(.015f);return bounds.Contains(point);}

    static Part ExportPart(Renderer renderer, Transform root, Texture2D atlas, Texture2D glow)
    {
        Mesh mesh = MeshOf(renderer); Need(mesh, "Native mesh: " + renderer.name);
        var skin = renderer as SkinnedMeshRenderer; var vertices = mesh.vertices; var uv = mesh.uv;
        var part = new Part {
            name = renderer.name, sourceRendererPath = PathOf(renderer.transform, root), sourceMeshPath = AssetDatabase.GetAssetPath(mesh),
            skinned = skin, vertices = vertices.SelectMany(V).ToArray(), normals = mesh.normals.SelectMany(V).ToArray(),
            uv = uv.SelectMany(p => new[] { p.x, p.y }).ToArray(), triangles = mesh.triangles,
            meshToRoot = M(root.worldToLocalMatrix * renderer.transform.localToWorldMatrix),
            rootToMesh = M(renderer.transform.worldToLocalMatrix * root.localToWorldMatrix),
            sourceColors = uv.SelectMany(p => { Color c = Pixel(atlas,p); return new[] { c.r,c.g,c.b,c.a }; }).ToArray(),
            sourceEmission = uv.Select(p => Pixel(glow,p).maxColorComponent).ToArray(),
            paletteCells = uv.SelectMany(p => new[] { Mathf.Clamp(Mathf.FloorToInt(p.x*16),0,15),15-Mathf.Clamp(Mathf.FloorToInt(p.y*16),0,15) }).ToArray()
        };
        if (skin)
        {
            var weights = mesh.boneWeights;
            part.boneIndices = weights.SelectMany(w => new[] {w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3}).ToArray();
            part.boneWeights = weights.SelectMany(w => new[] {w.weight0,w.weight1,w.weight2,w.weight3}).ToArray();
            part.bindposes = mesh.bindposes.SelectMany(M).ToArray();
            part.boneRestPivots = mesh.bindposes.SelectMany(b => V(b.inverse.MultiplyPoint3x4(Vector3.zero))).ToArray();
            part.boneNames = skin.bones.Select(t => t.name).ToArray(); part.bonePaths = skin.bones.Select(t => PathOf(t,root)).ToArray();
        }
        else { part.boneIndices = Array.Empty<int>(); part.boneWeights = Array.Empty<float>(); part.bindposes = Array.Empty<float>(); part.boneRestPivots = Array.Empty<float>(); part.boneNames=Array.Empty<string>(); part.bonePaths=Array.Empty<string>(); }
        return part;
    }

    static Mesh MeshOf(Renderer renderer)
    {
        if(renderer is SkinnedMeshRenderer skin)return skin.sharedMesh;
        // Unity's destroyed/missing component sentinel is not CLR null. In
        // particular an authored LineRenderer has no MeshFilter, so ?. would
        // attempt to dereference Unity's missing-component wrapper here.
        var filter=renderer.GetComponent<MeshFilter>();return filter?filter.sharedMesh:null;
    }
    static string PathOf(Transform item, Transform root) => item == root ? "" : AnimationUtility.CalculateTransformPath(item, root);
    static float[] V(Vector3 v) => new[] { v.x,v.y,v.z };
    static float[] M(Matrix4x4 m) => new[] {m.m00,m.m01,m.m02,m.m03,m.m10,m.m11,m.m12,m.m13,m.m20,m.m21,m.m22,m.m23,m.m30,m.m31,m.m32,m.m33};
    static Texture2D Decode(string path) { var texture = new Texture2D(2,2,TextureFormat.RGBA32,false,true); Need(texture.LoadImage(File.ReadAllBytes(path),false),"Read source atlas without importer mutation"); return texture; }
    static Color Pixel(Texture2D t, Vector2 uv) => t.GetPixel(Mathf.Clamp(Mathf.FloorToInt(uv.x*t.width),0,t.width-1),Mathf.Clamp(Mathf.FloorToInt(uv.y*t.height),0,t.height-1));
    static void Guard() { Need(Application.dataPath.Replace('\\','/').StartsWith("/private/tmp/adventure-trio-knight-",StringComparison.Ordinal),"Author only in isolated Knight remodel project"); Need(!Application.isPlaying,"Editor authoring only"); }
    static void Need(bool pass,string message) { if(!pass) throw new InvalidOperationException("SILVER_SENTINEL: "+message); }
}
