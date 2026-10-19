using System;
using System.IO;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class PlayerExpressionAuthoring
    {
        const string Data = "Assets/MazeParty/Resources/MazeParty/Expressions";
        const string Models = "Assets/MazeParty/Prefabs/Multiplayer/Expressions";
        const string PartyPack = "Assets/Ignore/Pack_PartyCharacters/Resources";
        const string LegacyPartyPack = "Assets/Ignore/FREE/Pack_FREE_PartyCharacters/Resources";
        static readonly string[] GestureNames =
        {
            "GREETING", "SALUTE", "INSULT", "HEART",
            "SURPRISE", "SURRENDER", "PLEADING", "EYES COVER"
        };
        const string WheelTitleSource = "HAND EMOTES";
        const string WheelHelpSource =
            "HOLD T + DRAG  /  RELEASE TO USE\nCENTER / ESC / RMB: CANCEL";
        [MenuItem("MazeParty/Player/Upgrade Expressions")]
        public static void Upgrade()
        {
            Directory.CreateDirectory(Data); Directory.CreateDirectory(Models); AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerExpressionCatalog>(Data + "/PlayerExpressions.asset");
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PlayerExpressionCatalog>();
                ConfigurePartyPackAppearance(catalog);
                ConfigureGestures(catalog);
                AssetDatabase.CreateAsset(catalog, Data + "/PlayerExpressions.asset");
            }
            else if (IsLegacyAppearanceCatalog(catalog) || IsLimitedPartyPackCatalog(catalog))
            {
                ConfigurePartyPackAppearance(catalog);
                EditorUtility.SetDirty(catalog);
            }
            if (catalog.Gestures == null ||
                catalog.Gestures.Length != GestureNames.Length ||
                !catalog.Gestures.Select(gesture =>
                        gesture != null ? gesture.Name : string.Empty)
                    .SequenceEqual(GestureNames))
            {
                ConfigureGestures(catalog);
                EditorUtility.SetDirty(catalog);
            }
            foreach (string path in new[] { "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab", BoardCanvasSplitProjectSetup.HudPrefabPath })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool lobby = path.Contains("LobbyCanvas");
                    EnsureWheel(root, lobby);
                    if (lobby)
                    {
                        EnsureLobbySelectors(root);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
        static T RequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException("Required Party Characters asset is missing: " + path);
            }
            return asset;
        }
        static bool IsLegacyAppearanceCatalog(PlayerExpressionCatalog catalog)
        {
            return catalog.Faces != null && catalog.Faces.Length == 4 &&
                   (catalog.Hats == null || catalog.Hats.Length == 0) &&
                   catalog.Faces.Select(face => face != null ? face.Name : string.Empty)
                       .SequenceEqual(new[] { "Neutral", "Happy", "Angry", "Surprised" });
        }
        static bool IsLimitedPartyPackCatalog(PlayerExpressionCatalog catalog)
        {
            return IsLimitedPartyPackCatalog(catalog, PartyPack) ||
                   IsLimitedPartyPackCatalog(catalog, LegacyPartyPack);
        }
        static bool IsLimitedPartyPackCatalog(
            PlayerExpressionCatalog catalog,
            string sourceRoot)
        {
            if (catalog.Faces == null || catalog.Faces.Length != 3 ||
                catalog.Hats == null || catalog.Hats.Length != 3)
            {
                return false;
            }

            return catalog.Faces.Select(face => face != null ? face.Name : string.Empty)
                       .SequenceEqual(new[] { "Face1", "Face2", "Face3" }) &&
                   catalog.Faces.Select(face => face != null
                           ? AssetDatabase.GetAssetPath(face.Sprite)
                           : string.Empty)
                       .SequenceEqual(Enumerable.Range(1, 3).Select(index =>
                           sourceRoot + "/Materials/Face Images/face " + index + ".png")) &&
                   catalog.Hats.Select(hat => hat != null ? hat.Name : string.Empty)
                       .SequenceEqual(new[] { "Hat1", "Hat2", "Hat3" }) &&
                   catalog.Hats.Select(hat => hat != null
                           ? AssetDatabase.GetAssetPath(hat.Prefab)
                           : string.Empty)
                       .SequenceEqual(new[]
                       {
                           sourceRoot + "/Prefabs/Hats/chef hat.prefab",
                           sourceRoot + "/Prefabs/Hats/orange fedora.prefab",
                           sourceRoot + "/Prefabs/Hats/party hat.prefab"
                       });
        }
        static void ConfigurePartyPackAppearance(PlayerExpressionCatalog catalog)
        {
            catalog.NoHatPreview = RequiredAsset<Sprite>("Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Close.png");
            var names = Enumerable.Range(1, 15).Select(index => "Face" + index).ToArray();
            catalog.Faces = new PlayerExpressionCatalog.Face[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                catalog.Faces[i] = new PlayerExpressionCatalog.Face
                {
                    Name = names[i],
                    Sprite = RequiredAsset<Sprite>(PartyPack + "/Materials/Face Images/face " + (i + 1) + ".png")
                };
            }
            var hatNames = Enumerable.Range(1, 30).Select(index => "Hat" + index).ToArray();
            var hatFiles = new[]
            {
                "chef hat", "orange fedora", "party hat", "alien", "angle hole",
                "bandage", "bonus", "clown", "cowboy hat", "egg", "fez",
                "fireman hat", "goat horns", "hair", "hat", "headphone",
                "heart antenna", "horn", "king crown", "mushroom hat", "noel hat",
                "party crown", "pineapple", "pump", "soldier hat", "sombrero",
                "top hat", "traffic cone", "viking helmet", "witch hat"
            };
            var hatY = Enumerable.Repeat(-0.33f, hatFiles.Length).ToArray();
            hatY[1] = -0.31f;
            hatY[2] = -0.34f;
            catalog.Hats = new PlayerExpressionCatalog.Hat[hatNames.Length];
            for (int i = 0; i < hatNames.Length; i++)
            {
                catalog.Hats[i] = new PlayerExpressionCatalog.Hat
                {
                    Name = hatNames[i],
                    Preview = AssetDatabase.LoadAssetAtPath<Sprite>(WardrobePreviewBaker.PreviewFolder + "/Hat" + (i + 1).ToString("00") + ".png"),
                    Prefab = RequiredAsset<GameObject>(PartyPack + "/Prefabs/Hats/" + hatFiles[i] + ".prefab"),
                    LocalPosition = new Vector3(0f, hatY[i], 0f),
                    LocalScale = Vector3.one
                };
            }
        }
        static void ConfigureGestures(PlayerExpressionCatalog catalog)
        {
            var faces = new byte[] { 0, 3, 1, 7, 9, 8, 2, 5 };
            catalog.Gestures = GestureNames.Select((name, index) =>
                new PlayerExpressionCatalog.Gesture { Name = name, ExpressionId = faces[index] }).ToArray();
        }
        static Sprite Sprite(string name, Func<float,float,Color> pixel)
        {
            string path=Data+"/"+name+".png";
            if (!File.Exists(path))
            {
                var tex=new Texture2D(256,256,TextureFormat.RGBA32,false);var colors=new Color[256*256];
                for(int y=0;y<256;y++)for(int x=0;x<256;x++) colors[y*256+x]=pixel((x+.5f)/256f-.5f,(y+.5f)/256f-.5f);
                tex.SetPixels(colors);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spritePixelsPerUnit=256;
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);
                importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static GameObject Hands(int id)
        {
            string path=Models+"/"+new[]{"ThumbsUp","Peace","Heart"}[id]+".prefab";
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Models+"/Hands.mat");
            if(mat==null){mat=new Material(Shader.Find("MazeParty/Toon Shader FX"));mat.color=Color.white;AssetDatabase.CreateAsset(mat,Models+"/Hands.mat");}
            var root=new GameObject("Gesture Hands");
            for(int side=-1;side<=1;side+=2)
            {
                var hand=new GameObject(side<0?"Left Hand":"Right Hand").transform;hand.SetParent(root.transform,false);
                hand.localPosition=new Vector3(side*(id==2?.14f:.4f),.12f,0);
                Part(hand,"Palm",new Vector3(0,0,0),new Vector3(.23f,.25f,.13f),mat);
                if(id==0)
                {
                    Part(hand,"Raised Thumb",new Vector3(-side*.12f,.16f,0),new Vector3(.08f,.27f,.09f),mat);
                    for(int f=0;f<4;f++)Part(hand,"Curled Finger "+f,new Vector3(side*.07f,.08f-f*.05f,.045f),new Vector3(.13f,.052f,.08f),mat);
                }
                else if(id==1)
                {
                    var a=Part(hand,"Index",new Vector3(-.065f,.22f,0),new Vector3(.075f,.32f,.08f),mat);a.localRotation=Quaternion.Euler(0,0,14);
                    var b=Part(hand,"Middle",new Vector3(.065f,.22f,0),new Vector3(.075f,.32f,.08f),mat);b.localRotation=Quaternion.Euler(0,0,-14);
                    Part(hand,"Folded Fingers",new Vector3(0,-.02f,.07f),new Vector3(.17f,.12f,.09f),mat);
                }
                else
                {
                    hand.localRotation=Quaternion.Euler(0,0,side*24);
                    var finger=Part(hand,"Curved Index",new Vector3(-side*.06f,.18f,0),new Vector3(.1f,.3f,.09f),mat);finger.localRotation=Quaternion.Euler(0,0,-side*40);
                    var thumb=Part(hand,"Heart Thumb",new Vector3(-side*.06f,-.12f,0),new Vector3(.08f,.24f,.08f),mat);thumb.localRotation=Quaternion.Euler(0,0,side*40);
                }
            }
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,path);UnityEngine.Object.DestroyImmediate(root);return prefab;
        }
        static Transform Part(Transform parent,string name,Vector3 pos,Vector3 size,Material mat)
        { var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=mat;return go.transform; }
        static RectTransform Rect(Transform parent,string name,Vector2 size,Vector2 pos)
        {var go=new GameObject(name,typeof(RectTransform));go.layer=LayerMask.NameToLayer("UI");var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;r.sizeDelta=size;r.anchoredPosition=pos;return r;}
        static Text Label(Transform parent,string name,string text,Vector2 size,Vector2 pos,int font=18)
        {var t=Rect(parent,name,size,pos).gameObject.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=font;t.text=text;t.alignment=TextAnchor.MiddleCenter;t.color=Color.white;t.raycastTarget=false;return t;}
        static void SectionLabel(Transform parent,string source)
        {
            if(parent.Find(source+" Label")!=null)return;
            var label=Label(parent,source+" Label",source,new Vector2(472,28),Vector2.zero,20);
            label.alignment=TextAnchor.MiddleLeft;label.color=new Color(.84f,.8f,1f);
            var layout=label.gameObject.AddComponent<LayoutElement>();layout.preferredHeight=28;
            label.gameObject.AddComponent<LocalizedText>().Configure(source);
        }
        static void Bind(SerializedObject data,string field,UnityEngine.Object value){data.FindProperty(field).objectReferenceValue=value;}
        static void EnsureWheel(GameObject canvas, bool lobby)
        {
            var old = canvas.GetComponent<HandEmoteWheelView>();
            if (old != null && old.HasRequiredReferences)
            {
                var existingPanel =
                    canvas.transform.Find("Hand Emote Wheel");
                if (!lobby)
                {
                    EnsureBoardWheelUsesHudCanvas(existingPanel);
                }
                EnsureWheelLocalization(existingPanel);
                return;
            }

            if (old != null)
            {
                var oldPanel = canvas.transform.Find("Hand Emote Wheel");
                if (oldPanel != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldPanel.gameObject);
                }
                UnityEngine.Object.DestroyImmediate(old);
            }

            var view = canvas.AddComponent<HandEmoteWheelView>();
            var panel = Rect(
                canvas.transform,
                "Hand Emote Wheel",
                Vector2.zero,
                Vector2.zero);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            var blocker = panel.gameObject.AddComponent<Image>();
            blocker.color = new Color(0, 0, 0, .12f);
            if (lobby)
            {
                panel.gameObject.AddComponent<GraphicRaycaster>();
                // The lobby wheel still needs its own sorting surface above
                // the independently-authored lobby panels.
                var overlay = panel.GetComponent<Canvas>() ??
                              panel.gameObject.AddComponent<Canvas>();
                overlay.overrideSorting = true;
                overlay.sortingOrder = 100;
            }
            else
            {
                EnsureBoardWheelUsesHudCanvas(panel);
            }

            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.blocksRaycasts = true;
            var sectorSprite = Sprite(
                "WheelSector8",
                (x, y) =>
                {
                    var r = Mathf.Sqrt(x * x + y * y);
                    var a = Mathf.Atan2(x, y) * Mathf.Rad2Deg;
                    return r > .12f && r < .49f && Mathf.Abs(a) < 20f
                        ? Color.white
                        : Color.clear;
                });
            var dotSprite = Sprite(
                "WheelDot",
                (x, y) => x * x + y * y < .24f
                    ? Color.white
                    : Color.clear);
            var data = new SerializedObject(view);
            data.FindProperty("lobbyWheel").boolValue = lobby;
            Bind(data, "panel", panel.gameObject);
            var sectors = data.FindProperty("sectors");
            sectors.arraySize = GestureNames.Length;
            var labels = data.FindProperty("labels");
            labels.arraySize = GestureNames.Length;
            for (var i = 0; i < GestureNames.Length; i++)
            {
                var angle = i * 45 * Mathf.Deg2Rad;
                var sector = Rect(
                        panel,
                        "Sector " + i,
                        new Vector2(380, 380),
                        Vector2.zero)
                    .gameObject.AddComponent<Image>();
                sector.sprite = sectorSprite;
                sector.raycastTarget = false;
                sector.rectTransform.localRotation =
                    Quaternion.Euler(0, 0, -i * 45);
                sector.color = new Color(.07f, .12f, .18f, .96f);
                sectors.GetArrayElementAtIndex(i).objectReferenceValue =
                    sector;
                var label = Label(
                    panel,
                    "Gesture " + i,
                    GestureNames[i],
                    new Vector2(105, 32),
                    new Vector2(
                        Mathf.Sin(angle) * 142,
                        Mathf.Cos(angle) * 142),
                    13);
                labels.GetArrayElementAtIndex(i).objectReferenceValue =
                    label;
            }

            var center = Rect(
                    panel,
                    "Center Cancel Zone",
                    new Vector2(86, 86),
                    Vector2.zero)
                .gameObject.AddComponent<Image>();
            center.sprite = dotSprite;
            center.color = new Color(.05f, .08f, .12f, 1);
            center.raycastTarget = false;
            Label(
                panel,
                "Center Label",
                "T",
                new Vector2(48, 40),
                Vector2.zero,
                24);
            Label(
                panel,
                "Title",
                WheelTitleSource,
                new Vector2(360, 30),
                new Vector2(0, 205),
                23);
            Label(
                panel,
                "Help",
                WheelHelpSource,
                new Vector2(440, 50),
                new Vector2(0, -220),
                16);
            var selected = Label(
                panel,
                "Selection",
                "DRAG TO SELECT",
                new Vector2(390, 28),
                new Vector2(0, -181),
                18);
            Bind(data, "selectionText", selected);
            var pointer = Rect(
                panel,
                "Selection Pointer",
                new Vector2(14, 14),
                Vector2.zero);
            var dot = pointer.gameObject.AddComponent<Image>();
            dot.sprite = dotSprite;
            dot.color = new Color(1, .84f, .3f);
            dot.raycastTarget = false;
            Bind(data, "pointer", pointer);
            data.ApplyModifiedPropertiesWithoutUndo();
            EnsureWheelLocalization(panel);
            panel.gameObject.SetActive(false);
        }

        static void EnsureBoardWheelUsesHudCanvas(Transform panel)
        {
            if (panel == null)
            {
                throw new InvalidOperationException(
                    "Hand Emote Wheel panel is missing from BoardHudCanvas.");
            }

            var nestedRaycaster = panel.GetComponent<GraphicRaycaster>();
            if (nestedRaycaster != null)
            {
                UnityEngine.Object.DestroyImmediate(nestedRaycaster);
            }

            var nestedCanvas = panel.GetComponent<Canvas>();
            if (nestedCanvas != null)
            {
                UnityEngine.Object.DestroyImmediate(nestedCanvas);
            }

            // A final sibling renders last inside the authored HUD Canvas, so
            // the board wheel needs no extra Canvas or raycaster.
            panel.SetAsLastSibling();
        }

        static void EnsureWheelLocalization(Transform panel)
        {
            if (panel == null)
            {
                throw new InvalidOperationException(
                    "Hand Emote Wheel panel is missing from its authored Canvas prefab.");
            }

            EnsureLocalizedLabel(panel, "Title", WheelTitleSource);
            EnsureLocalizedLabel(panel, "Help", WheelHelpSource);
        }
        static void EnsureLocalizedLabel(
            Transform parent,
            string childName,
            string source)
        {
            var child = parent.Find(childName);
            var label = child != null ? child.GetComponent<Text>() : null;
            if (label == null)
            {
                throw new InvalidOperationException(
                    "Hand Emote Wheel/" + childName + " Text is missing.");
            }

            label.text = source;
            var localized = label.GetComponent<LocalizedText>() ??
                            label.gameObject.AddComponent<LocalizedText>();
            localized.Configure(source);
        }
        internal static void EnsureLobbySelectors(GameObject canvas)
        {
            EnsureImageSelector(canvas, false);
            EnsureImageSelector(canvas, true);
        }

        static void EnsureImageSelector(GameObject canvas, bool hat)
        {
            var existing = hat ? (MonoBehaviour)canvas.GetComponentInChildren<LobbyHatView>(true)
                : canvas.GetComponentInChildren<LobbyExpressionView>(true);
            if (existing != null)
            {
                if (hat ? !((LobbyHatView)existing).HasRequiredReferences
                    : !((LobbyExpressionView)existing).HasRequiredReferences)
                    throw new InvalidOperationException("Incomplete wardrobe image selector bindings");
                return;
            }
            var footer = canvas.GetComponentsInChildren<Transform>(true).Single(x => x.name == "Customization Footer");
            var row = Rect(footer, hat ? "Hat Selection" : "Face Expression", new Vector2(472, 100), Vector2.zero);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
            var view = hat ? (MonoBehaviour)row.gameObject.AddComponent<LobbyHatView>()
                : row.gameObject.AddComponent<LobbyExpressionView>();
            var data = new SerializedObject(view);
            Bind(data, "lobby", canvas.GetComponent<OnlineLobbyView>());
            foreach (bool previous in new[] { true, false })
            {
                var r = Rect(row, previous ? "Previous" : "Next", new Vector2(48, 48), new Vector2(previous ? -150 : 150, 0));
                var image = r.gameObject.AddComponent<Image>(); image.color = new Color(.12f, .25f, .32f);
                var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image;
                r.gameObject.AddComponent<UiSoundEmitter>().Configure(SoundKeys.UiHover, SoundKeys.UiClick);
                Label(r, "Arrow", previous ? "<" : ">", new Vector2(28, 28), Vector2.zero);
                Bind(data, previous ? "previous" : "next", button);
            }
            if (!hat)
            {
                var background = Rect(row, "Face Preview Background", new Vector2(88, 88), Vector2.zero).gameObject.AddComponent<Image>();
                background.color = new Color(.82f, .86f, .9f); background.raycastTarget = false;
            }
            var preview = Rect(row, hat ? "Hat Preview" : "Face Preview", new Vector2(88, 88), Vector2.zero).gameObject.AddComponent<Image>();
            preview.preserveAspect = true; preview.raycastTarget = false;
            var catalog = PlayerExpressionCatalog.Instance;
            preview.sprite = hat ? catalog.NoHatPreview : catalog.Faces[0].Sprite;
            Bind(data, "preview", preview); data.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("MazeParty/Player/Apply Image Wardrobe Layout")]
        public static void ApplyImageWardrobeLayout()
        {
            const string path = "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var footer = root.GetComponentsInChildren<Transform>(true).Single(x => x.name == "Customization Footer");
                foreach (var name in new[] { "Face Label", "Hat Label", "Emote Faces Label", "Face Expression", "Hat Selection", "Emote Face Mapping" })
                {
                    var child = footer.Find(name);
                    if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                footer.GetComponent<LayoutElement>().preferredHeight = 210;
                var panel = (RectTransform)root.GetComponentsInChildren<Transform>(true).Single(x => x.name == "Player Customization");
                panel.sizeDelta = new Vector2(520, 440);
                EnsureLobbySelectors(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
