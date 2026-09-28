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
        const string PartyPack = "Assets/Ignore/FREE/Pack_FREE_PartyCharacters/Resources";
        [MenuItem("MazeParty/Player/Upgrade Expressions")]
        public static void Upgrade()
        {
            Directory.CreateDirectory(Data); Directory.CreateDirectory(Models); AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerExpressionCatalog>(Data + "/PlayerExpressions.asset");
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PlayerExpressionCatalog>();
                ConfigurePartyPackAppearance(catalog);
                var names = new[] { "THUMBS UP", "PEACE", "HEART" };
                catalog.Gestures = new PlayerExpressionCatalog.Gesture[3];
                for (int i = 0; i < 3; i++) catalog.Gestures[i] = new PlayerExpressionCatalog.Gesture { Name = names[i], HandsPrefab = Hands(i) };
                AssetDatabase.CreateAsset(catalog, Data + "/PlayerExpressions.asset");
            }
            else if (IsLegacyAppearanceCatalog(catalog))
            {
                ConfigurePartyPackAppearance(catalog);
                EditorUtility.SetDirty(catalog);
            }
            foreach (string path in new[] { "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab", "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab" })
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
        static void ConfigurePartyPackAppearance(PlayerExpressionCatalog catalog)
        {
            var names = new[] { "Face1", "Face2", "Face3" };
            catalog.Faces = new PlayerExpressionCatalog.Face[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                catalog.Faces[i] = new PlayerExpressionCatalog.Face
                {
                    Name = names[i],
                    Sprite = RequiredAsset<Sprite>(PartyPack + "/Materials/Face Images/face " + (i + 1) + ".png")
                };
            }
            var hatNames = new[] { "Hat1", "Hat2", "Hat3" };
            var hatFiles = new[] { "chef hat", "orange fedora", "party hat" };
            var hatY = new[] { -0.33f, -0.31f, -0.34f };
            catalog.Hats = new PlayerExpressionCatalog.Hat[hatNames.Length];
            for (int i = 0; i < hatNames.Length; i++)
            {
                catalog.Hats[i] = new PlayerExpressionCatalog.Hat
                {
                    Name = hatNames[i],
                    Prefab = RequiredAsset<GameObject>(PartyPack + "/Prefabs/Hats/" + hatFiles[i] + ".prefab"),
                    LocalPosition = new Vector3(0f, hatY[i], 0f),
                    LocalScale = Vector3.one
                };
            }
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
            if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=Color.white;AssetDatabase.CreateAsset(mat,Models+"/Hands.mat");}
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
        static void Bind(SerializedObject data,string field,UnityEngine.Object value){data.FindProperty(field).objectReferenceValue=value;}
        static void EnsureWheel(GameObject canvas,bool lobby)
        {
            var old=canvas.GetComponent<HandEmoteWheelView>();
            if(old!=null){if(!old.HasRequiredReferences)throw new InvalidOperationException("Incomplete emote bindings");return;}
            var view=canvas.AddComponent<HandEmoteWheelView>();
            var panel=Rect(canvas.transform,"Hand Emote Wheel",Vector2.zero,Vector2.zero);
            panel.anchorMin=Vector2.zero;panel.anchorMax=Vector2.one;
            var blocker=panel.gameObject.AddComponent<Image>();blocker.color=new Color(0,0,0,.12f);
            panel.gameObject.AddComponent<GraphicRaycaster>();
            // Own nested sorting canvas keeps this wheel above the lobby panels without changing the camera.
            var overlay=panel.gameObject.AddComponent<Canvas>();overlay.overrideSorting=true;overlay.sortingOrder=100;
            var group=panel.gameObject.AddComponent<CanvasGroup>();group.ignoreParentGroups=true;group.blocksRaycasts=true;
            var sectorSprite=Sprite("WheelSector",(x,y)=>{float r=Mathf.Sqrt(x*x+y*y);float a=Mathf.Atan2(x,y)*Mathf.Rad2Deg;return r>.12f&&r<.49f&&Mathf.Abs(a)<58f?Color.white:Color.clear;});
            var dotSprite=Sprite("WheelDot",(x,y)=>x*x+y*y<.24f?Color.white:Color.clear);
            var data=new SerializedObject(view);data.FindProperty("lobbyWheel").boolValue=lobby;Bind(data,"panel",panel.gameObject);
            var sectors=data.FindProperty("sectors");sectors.arraySize=3;var labels=data.FindProperty("labels");labels.arraySize=3;
            for(int i=0;i<3;i++)
            {
                float angle=i*120*Mathf.Deg2Rad;
                var sector=Rect(panel,"Sector "+i,new Vector2(360,360),Vector2.zero).gameObject.AddComponent<Image>();sector.sprite=sectorSprite;sector.raycastTarget=false;sector.rectTransform.localRotation=Quaternion.Euler(0,0,-i*120);sector.color=new Color(.07f,.12f,.18f,.96f);
                sectors.GetArrayElementAtIndex(i).objectReferenceValue=sector;
                var label=Label(panel,"Gesture "+i,new[]{"THUMBS UP","PEACE","HEART"}[i],new Vector2(125,40),new Vector2(Mathf.Sin(angle)*112,Mathf.Cos(angle)*112),17);
                labels.GetArrayElementAtIndex(i).objectReferenceValue=label;
            }
            var center=Rect(panel,"Center Cancel Zone",new Vector2(86,86),Vector2.zero).gameObject.AddComponent<Image>();center.sprite=dotSprite;center.color=new Color(.05f,.08f,.12f,1);center.raycastTarget=false;
            Label(panel,"Center Label","T",new Vector2(48,40),Vector2.zero,24);
            Label(panel,"Title","HAND EMOTES",new Vector2(360,30),new Vector2(0,205),23);
            Label(panel,"Help","HOLD T + DRAG  /  RELEASE TO USE\nCENTER / ESC / RMB: CANCEL",new Vector2(440,50),new Vector2(0,-220),16);
            var selected=Label(panel,"Selection","DRAG TO SELECT",new Vector2(390,28),new Vector2(0,-181),18);Bind(data,"selectionText",selected);
            var pointer=Rect(panel,"Selection Pointer",new Vector2(14,14),Vector2.zero);var dot=pointer.gameObject.AddComponent<Image>();dot.sprite=dotSprite;dot.color=new Color(1,.84f,.3f);dot.raycastTarget=false;Bind(data,"pointer",pointer);
            data.ApplyModifiedPropertiesWithoutUndo();panel.gameObject.SetActive(false);
        }
        static void EnsureFaceSelector(GameObject canvas)
        {
            if(canvas.GetComponentInChildren<LobbyExpressionView>(true)!=null)return;
            var lobby=canvas.GetComponent<OnlineLobbyView>();var footer=canvas.GetComponentsInChildren<Transform>(true).Single(x=>x.name=="Customization Footer");
            var row=Rect(footer,"Face Expression",new Vector2(230,32),Vector2.zero);var layout=row.gameObject.AddComponent<LayoutElement>();layout.preferredWidth=230;layout.preferredHeight=32;
            var view=row.gameObject.AddComponent<LobbyExpressionView>();var data=new SerializedObject(view);Bind(data,"lobby",lobby);
            foreach(bool previous in new[]{true,false})
            {
                var r=Rect(row,previous?"Previous":"Next",new Vector2(30,30),new Vector2(previous?-99:99,0));var image=r.gameObject.AddComponent<Image>();image.color=new Color(.12f,.25f,.32f);var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;
                Label(r,"Arrow",previous?"<":">",new Vector2(28,28),Vector2.zero);Bind(data,previous?"previous":"next",button);
            }
            var previewBackground=Rect(row,"Face Preview Background",new Vector2(34,32),new Vector2(-65,0)).gameObject.AddComponent<Image>();previewBackground.color=new Color(.82f,.86f,.9f);previewBackground.raycastTarget=false;
            var preview=Rect(row,"Face Preview",new Vector2(32,32),new Vector2(-65,0)).gameObject.AddComponent<Image>();preview.color=Color.white;preview.raycastTarget=false;Bind(data,"preview",preview);
            Bind(data,"title",Label(row,"Face Name","Face1",new Vector2(125,30),new Vector2(17,0),16));data.ApplyModifiedPropertiesWithoutUndo();
        }
        internal static void EnsureLobbySelectors(GameObject canvas)
        {
            EnsureFaceSelector(canvas);
            EnsureHatSelector(canvas);
        }
        static void EnsureHatSelector(GameObject canvas)
        {
            var legacy=canvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(x=>x.name=="Test Hat Toggle");
            if(legacy!=null)UnityEngine.Object.DestroyImmediate(legacy.gameObject);
            var existing=canvas.GetComponentInChildren<LobbyHatView>(true);
            if(existing!=null){if(!existing.HasRequiredReferences)throw new InvalidOperationException("Incomplete hat selector bindings");return;}
            var lobby=canvas.GetComponent<OnlineLobbyView>();var footer=canvas.GetComponentsInChildren<Transform>(true).Single(x=>x.name=="Customization Footer");
            var row=Rect(footer,"Hat Selection",new Vector2(230,32),Vector2.zero);var layout=row.gameObject.AddComponent<LayoutElement>();layout.preferredWidth=230;layout.preferredHeight=32;
            var view=row.gameObject.AddComponent<LobbyHatView>();var data=new SerializedObject(view);Bind(data,"lobby",lobby);
            foreach(bool previous in new[]{true,false})
            {
                var r=Rect(row,previous?"Previous":"Next",new Vector2(30,30),new Vector2(previous?-99:99,0));var image=r.gameObject.AddComponent<Image>();image.color=new Color(.12f,.25f,.32f);var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;
                Label(r,"Arrow",previous?"<":">",new Vector2(28,28),Vector2.zero);Bind(data,previous?"previous":"next",button);
            }
            Bind(data,"title",Label(row,"Hat Name","None",new Vector2(175,30),Vector2.zero,16));data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
