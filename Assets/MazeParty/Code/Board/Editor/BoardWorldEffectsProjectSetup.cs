using System;
using System.IO;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class BoardWorldEffectsProjectSetup
    {
        public const string MarkerPath = "Assets/MazeParty/Prefabs/Board/UI/BoardTileEffectWorld.prefab";
        public const string TornadoPath = "Assets/MazeParty/Prefabs/Common/VFX/FireTornado.prefab";
        private const string AnimationFolder = "Assets/MazeParty/Animations/Board";
        private const string CuePath = "Assets/MazeParty/Audio/Cues/Board/board.fire_tornado.asset";
        private const string BoardScene = "Assets/MazeParty/Scenes/Board/Board.unity";

        [MenuItem("MazeParty/Board/Install World Tile Feedback")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = SceneManager.GetSceneByPath(BoardScene);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("Save the Board scene before installation.");
            if (opened) scene = EditorSceneManager.OpenScene(BoardScene, OpenSceneMode.Additive);
            try
            {
                foreach (var presenter in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BoardFlowCameraPresenter>(true)))
                    EnsureFor(presenter.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static void EnsureFor(GameObject root)
        {
            var marker = EnsureMarker();
            var tornado = EnsureTornado();
            var fireSound = EnsureFireSound();
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab").GetComponentInChildren<BoardMinimapView>(true);
            var presenter = root.GetComponent<BoardWorldEffectsPresenter>();
            if (presenter == null) presenter = root.AddComponent<BoardWorldEffectsPresenter>();
            var data = new SerializedObject(presenter);
            SetMissing(data.FindProperty("markerPrefab"), marker);
            SetMissing(data.FindProperty("mapAppearance"), map);
            var impacts = data.FindProperty("impacts");
            impacts.arraySize = 3;
            var prefabs = new[] { tornado, SharedVfxProjectSetup.EnsureLightningStrikePrefab(), SharedVfxProjectSetup.EnsureCartoonExplosionPrefab() };
            var sounds = new[] { fireSound,
                AssetDatabase.LoadAssetAtPath<SoundCue>("Assets/MazeParty/Audio/Cues/Misc/lightning.asset"),
                AssetDatabase.LoadAssetAtPath<SoundCue>("Assets/MazeParty/Audio/Cues/Item/item.explosion.asset") };
            var scales = new[] { 1f, .35f, 1f };
            for (var i = 0; i < 3; i++)
            {
                var row = impacts.GetArrayElementAtIndex(i);
                if (row.FindPropertyRelative("prefab").objectReferenceValue == null)
                {
                    row.FindPropertyRelative("prefab").objectReferenceValue = prefabs[i];
                    row.FindPropertyRelative("scale").floatValue = scales[i];
                    row.FindPropertyRelative("height").floatValue = i == 2 ? 0f : -1f;
                }
                SetMissing(row.FindPropertyRelative("sound"), sounds[i]);
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            if (!presenter.HasRequiredReferences) throw new InvalidOperationException("Missing world tile feedback references.");
        }
        private static void SetMissing(SerializedProperty property, UnityEngine.Object value)
        {
            if (property.objectReferenceValue == null) property.objectReferenceValue = value;
        }
        private static BoardTileEffectWorldView EnsureMarker()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(MarkerPath);
            if (existing != null) return existing.GetComponent<BoardTileEffectWorldView>();
            var root = new GameObject("Board Tile Effect", typeof(RectTransform), typeof(Canvas), typeof(BoardTileEffectWorldView));
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(120f, 120f);
                rect.localScale = Vector3.one * .01f;
                var back = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapCircleGraphic));
                back.transform.SetParent(root.transform, false);
                ((RectTransform)back.transform).sizeDelta = new Vector2(120f, 120f);
                var circle = back.GetComponent<BoardMapCircleGraphic>();
                circle.color = new Color(0f, 0f, 0f, .82f); circle.raycastTarget = false;
                var foreground = new GameObject("Effect Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapIcon));
                foreground.transform.SetParent(root.transform, false);
                ((RectTransform)foreground.transform).sizeDelta = new Vector2(86f, 86f);
                var icon = foreground.GetComponent<BoardMapIcon>();
                icon.raycastTarget = false; icon.color = Color.white;
                var data = new SerializedObject(root.GetComponent<BoardTileEffectWorldView>());
                data.FindProperty("worldCanvas").objectReferenceValue = canvas;
                data.FindProperty("icon").objectReferenceValue = icon;
                data.FindProperty("backdrop").objectReferenceValue = circle;
                data.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, MarkerPath).GetComponent<BoardTileEffectWorldView>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static GameObject EnsureTornado()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(TornadoPath);
            if (existing != null) return existing;
            Directory.CreateDirectory(AnimationFolder);
            AssetDatabase.Refresh();
            var root = new GameObject("Fire Tornado One Shot");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/Fire Tornado.prefab");
                if (source == null) throw new InvalidOperationException("Fire Tornado source is missing.");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Visual"; model.transform.localScale = Vector3.one * .2f;
                foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                var clipPath = AnimationFolder + "/FireTornadoOneShot.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null)
                {
                    clip = new AnimationClip { name = "FireTornadoOneShot", frameRate = 30f };
                    foreach (var axis in new[] { "x", "y", "z" })
                        clip.SetCurve("Visual", typeof(Transform), "m_LocalScale." + axis,
                            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.12f, .2f),
                                new Keyframe(.75f, .2f), new Keyframe(1f, 0f)));
                    clip.SetCurve("Visual", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0f, 0f, 1f, 180f));
                    AssetDatabase.CreateAsset(clip, clipPath);
                }
                var controllerPath = AnimationFolder + "/FireTornadoOneShot.controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null)
                {
                    controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                    controller.AddMotion(clip);
                }
                var animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = controller;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var pooled = root.AddComponent<PooledOneShotVfx>();
                pooled.Configure(Array.Empty<ParticleSystem>(), Array.Empty<Light>(), 1.05f, 0f);
                var data = new SerializedObject(pooled);
                data.FindProperty("minimumPlaybackSeconds").floatValue = 1.05f;
                data.FindProperty("playbackAnimator").objectReferenceValue = animator;
                data.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, TornadoPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static SoundCue EnsureFireSound()
        {
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(CuePath);
            if (cue == null)
            {
                cue = ScriptableObject.CreateInstance<SoundCue>();
                cue.ConfigureDefaults(SoundKeys.BoardFireTornado, AudioChannel.Sfx, true, false,
                    .65f, 0f, .04f, 4, 0f, 85, false, .1f, 60f);
                cue.SetClips(new[] { AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/sound/explosion.mp3") });
                AssetDatabase.CreateAsset(cue, CuePath);
            }
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundSystemProjectSetup.LibraryPath);
            if (!library.TryGet(cue.Key, out var registered))
            {
                library.SetCues(library.Cues.Concat(new[] { cue }).ToArray());
                EditorUtility.SetDirty(library);
            }
            return cue;
        }
    }
}
