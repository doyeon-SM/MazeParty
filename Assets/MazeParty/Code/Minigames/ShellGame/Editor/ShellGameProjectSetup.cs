using System;
using System.Linq;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class ShellGameProjectSetup
    {
        public const string ScenePath="Assets/MazeParty/Scenes/Minigames/ShellGame/ShellGame.unity";
        public const string Folder="Assets/MazeParty/Prefabs/Minigames/ShellGame";
        public const string EnvironmentPath=Folder+"/ShellGameEnvironment.prefab";
        public const string HudPath=Folder+"/UI/ShellGameHud.prefab";
        public const string BoxPath=Folder+"/ShellGameBox.prefab";
        public const string CoinPath=Folder+"/ShellGameCoin.prefab";
        private const string CommonHudPath="Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab";
        private const string ScoreHudPath="Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab";
        [MenuItem("MazeParty/Minigames/Build Shell Game")]
        public static void BuildShellGameAssets()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            EnsureFolder(Folder+"/UI"); EnsureFolder("Assets/MazeParty/Scenes/Minigames/ShellGame");
            var hud=EnsureHud();
            var box=EnsureModel(BoxPath,"Assets/Ignore/Christmas Gifts/Prefabs/box1.prefab",1.7f,false);
            var coin=EnsureModel(CoinPath,"Assets/Ignore/BTM_Assets/BTM_Items_Gems/Prefabs/Coin.prefab",.9f,true);
            var environment=AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            if(environment==null)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(BalloonBlowProjectSetup.EnvironmentPrefabPath);
                if(source==null) throw new InvalidOperationException("BalloonBlow environment required.");
                var copy=UnityEngine.Object.Instantiate(source);
                try
                {
                    copy.name="Shell Game Environment"; copy.transform.position=Vector3.zero;
                    foreach(var child in copy.GetComponentsInChildren<Transform>(true))
                        if(child.name=="Backdrop" || child.name=="Rules Plaque" || child.name.StartsWith("Log_04"))
                            child.gameObject.SetActive(false);
                    foreach(var c in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                    environment=PrefabUtility.SaveAsPrefabAsset(copy,EnvironmentPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(copy); }
            }
            var previous=SceneManager.GetActiveScene();
            var scene=SceneManager.GetSceneByPath(ScenePath);
            var opened=!scene.IsValid() || !scene.isLoaded;
            if(!opened && scene.isDirty) throw new InvalidOperationException("Save ShellGame before setup.");
            var exists=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)!=null;
            if(opened) scene=exists ? EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive) :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                if(!exists)
                {
                    SceneManager.SetActiveScene(scene);
                    RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Night_01.mat");
                    var center=new Vector3(1300f,0f,0f);
                    var root=new GameObject("Shell Game Network State");
                    var arena=(GameObject)PrefabUtility.InstantiatePrefab(environment,root.transform);
                    arena.name="Shell Game Environment"; arena.transform.position=center;
                    var light=new GameObject("Shell Game Sun").AddComponent<Light>();
                    light.transform.SetParent(arena.transform,false); light.type=LightType.Directional; light.intensity=1.2f;
                    light.transform.rotation=Quaternion.Euler(48,-30,0);
                    var cameraObject=new GameObject("CM_ShellGameShared");
                    cameraObject.transform.SetParent(root.transform,false);
                    cameraObject.transform.position=center+new Vector3(0,11,-14);
                    cameraObject.transform.LookAt(center+Vector3.up);
                    var camera=cameraObject.AddComponent<CinemachineCamera>();
                    var lens=camera.Lens; lens.ModeOverride=LensSettings.OverrideModes.Orthographic;
                    lens.OrthographicSize=6.5f; lens.FarClipPlane=120f; camera.Lens=lens; camera.Priority=0;
                    var players=new GameObject("Runtime Players").transform; players.SetParent(root.transform,false);
                    var anchorRoot=new GameObject("Player Anchors").transform; anchorRoot.SetParent(root.transform,false);
                    var anchors=new Transform[4];
                    for(var i=0;i<4;i++)
                    {
                        anchors[i]=new GameObject("Player Anchor "+(i+1)).transform; anchors[i].SetParent(anchorRoot,false);
                        anchors[i].position=center+new Vector3((i-1.5f)*2.7f,0,-3.8f);
                        anchors[i].rotation=Quaternion.Euler(0,180,0);
                    }
                    var boxRoot=new GameObject("Shell Boxes").transform; boxRoot.SetParent(root.transform,false);
                    var boxes=new Transform[3]; var colliders=new Collider[3];
                    for(var i=0;i<3;i++)
                    {
                        boxes[i]=((GameObject)PrefabUtility.InstantiatePrefab(box,boxRoot)).transform;
                        boxes[i].name="Shell Box "+i; boxes[i].position=center+new Vector3((i-1)*3f,.05f,0);
                        colliders[i]=boxes[i].GetComponent<Collider>();
                    }
                    var coinObject=((GameObject)PrefabUtility.InstantiatePrefab(coin,root.transform)).transform;
                    var marker=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    marker.name="Local Selection Marker"; marker.transform.SetParent(root.transform,false);
                    marker.transform.localScale=new Vector3(2.1f,.015f,2.1f);
                    UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
                    var markerMaterialPath=Folder+"/SelectionMarker.mat";
                    var markerMaterial=AssetDatabase.LoadAssetAtPath<Material>(markerMaterialPath);
                    if(markerMaterial==null)
                    {
                        markerMaterial=new Material(Shader.Find("MazeParty/Toon Shader FX"));
                        markerMaterial.SetColor("_BaseColor",new Color(1f,.82f,.2f));
                        AssetDatabase.CreateAsset(markerMaterial,markerMaterialPath);
                    }
                    marker.GetComponent<Renderer>().sharedMaterial=markerMaterial;
                    var scores=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScoreHudPath),scene);
                    var selection=(GameObject)PrefabUtility.InstantiatePrefab(hud,scene);
                    var common=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CommonHudPath),scene);
                    EditorSceneManager.SaveScene(scene,ScenePath);
                    root.AddComponent<NetworkObject>(); var state=root.AddComponent<NetworkShellGameState>();
                    root.AddComponent<ShellGameNetworkView>().Configure(state,camera,arena,players,anchors,boxes,colliders,coinObject,
                        marker.transform,AssetDatabase.LoadAssetAtPath<GameObject>(MinigameVfxProjectSetup.ArrivalFireworksPrefabPath),scores.GetComponent<BouncingBallsHudBindings>(),selection.GetComponent<ShellGameHudBindings>(),
                        common.GetComponent<MinigameStartCountdownView>(),common.GetComponent<MinigameCommonHudView>(),center+Vector3.up*.05f);
                    EditorSceneManager.SaveScene(scene,ScenePath);
                }
                var view=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ShellGameNetworkView>(true)).Single();
                if(!view.HasRequiredReferences) throw new InvalidOperationException("ShellGame bindings incomplete.");
            }
            finally
            {
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if(opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
            }
            if(!EditorBuildSettings.scenes.Any(s=>s.path==ScenePath))
                EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(ScenePath,true)}).ToArray();
            EnsurePreviewSlot();
            SoundSystemProjectSetup.EnsureCues(); SoundSystemProjectSetup.RefreshLibraryMenu(); AssetDatabase.SaveAssets();
        }
        private static GameObject EnsureModel(string path,string source,float width,bool isCoin)
        {
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(existing!=null) return existing;
            var template=AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if(template==null) throw new InvalidOperationException("Missing "+source);
            var root=new GameObject(isCoin ? "Shell Game Coin" : "Shell Game Box");
            try
            {
                var model=UnityEngine.Object.Instantiate(template,root.transform);
                model.transform.localPosition=Vector3.zero; model.transform.localRotation=Quaternion.identity;
                foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
                foreach(var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                foreach(var body in model.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                var renderers=model.GetComponentsInChildren<Renderer>(true);
                foreach(var renderer in renderers) renderer.sharedMaterials=renderer.sharedMaterials.Select(ResolveModelMaterial).ToArray();
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                model.transform.localScale*=width/bounds.size.x;
                bounds=renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                model.transform.localPosition-=new Vector3(bounds.center.x,isCoin?bounds.center.y:bounds.min.y,bounds.center.z);
                if(!isCoin)
                {
                    var hit=root.AddComponent<BoxCollider>(); hit.size=bounds.size; hit.center=new Vector3(0,bounds.size.y*.5f,0);
                }
                return PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        internal static Material ResolveModelMaterial(Material source)
        {
            if(source==null || !source.shader.name.StartsWith("Legacy Shaders/")) return ToonShaderProjectSetup.Resolve(source);
            var path=Folder+"/GiftBox.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null) return material;
            material=new Material(Shader.Find("MazeParty/Toon Shader FX"));
            material.name="Gift Box";
            material.SetTexture("_BaseMap",source.mainTexture);
            material.SetTextureScale("_BaseMap",source.mainTextureScale);
            material.SetTextureOffset("_BaseMap",source.mainTextureOffset);
            var tint=source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            material.SetColor("_BaseColor",tint); material.SetColor("_Color",tint);
            if(source.HasProperty("_BumpMap"))
            {
                var normal=source.GetTexture("_BumpMap");
                material.SetTexture("_BumpMap",normal);
                if(normal!=null) material.EnableKeyword("_NORMALMAP");
            }
            AssetDatabase.CreateAsset(material,path);
            return material;
        }
        private static GameObject EnsureHud()
        {
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if(existing!=null)
            {
                if(existing.GetComponent<ShellGameHudBindings>()?.HasRequiredReferences!=true)
                    throw new InvalidOperationException("ShellGame HUD bindings incomplete.");
                return existing;
            }
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab")
                .GetComponent<BoardMapCanvasBindings>().MovesText;
            var root=new GameObject("Shell Game Selection Timer",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(LocalizedFontScope));
            try
            {
                var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=36;
                var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
                var seconds=UnityEngine.Object.Instantiate(source.gameObject,root.transform).GetComponent<Text>();
                seconds.name="Selection Seconds"; seconds.fontStyle=FontStyle.Normal; seconds.raycastTarget=false; seconds.text="10";
                root.AddComponent<ShellGameHudBindings>().Configure(canvas,seconds);
                return PrefabUtility.SaveAsPrefabAsset(root,HudPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void EnsurePreviewSlot()
        {
            const string path="Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var data=new SerializedObject(root.GetComponent<BoardHudCanvasBindings>());
                var clips=data.FindProperty("minigamePreviewClips");
                var count=BoardHudCanvasBindings.RequiredMinigamePreviewClipSlotCount;
                if(clips.arraySize>=count) return;
                var old=clips.arraySize; clips.arraySize=count;
                for(var i=old;i<count;i++) clips.GetArrayElementAtIndex(i).objectReferenceValue=null;
                data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            var split=path.LastIndexOf('/'); var parent=path.Substring(0,split);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent,path.Substring(split+1));
        }
    }
}
