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
        [MenuItem("MazeParty/Player/Upgrade Expressions")]
        public static void Upgrade()
        {
            Directory.CreateDirectory(Data); Directory.CreateDirectory(Models); AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerExpressionCatalog>(Data + "/PlayerExpressions.asset");
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PlayerExpressionCatalog>();
                var names = new[] { "Neutral", "Happy", "Angry", "Surprised" };
                catalog.Faces = new PlayerExpressionCatalog.Face[4];
                for (int i = 0; i < 4; i++)
                { int face = i; catalog.Faces[i] = new PlayerExpressionCatalog.Face { Name = names[i], Sprite = Sprite(names[i], (x,y) => FacePixel(face,x,y)) }; }
                names = new[] { "THUMBS UP", "PEACE", "HEART" };
                catalog.Gestures = new PlayerExpressionCatalog.Gesture[3];
                for (int i = 0; i < 3; i++) catalog.Gestures[i] = new PlayerExpressionCatalog.Gesture { Name = names[i], HandsPrefab = Hands(i) };
                AssetDatabase.CreateAsset(catalog, Data + "/PlayerExpressions.asset");
            }
            foreach (string path in new[] { "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab", "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool lobby = path.Contains("LobbyCanvas");
                    EnsureWheel(root, lobby);
                    if (lobby) EnsureFaceSelector(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
        static Color FacePixel(int id, float x, float y)
        {
            bool eye = Ellipse(x,y,-.21f,.13f,.04f,id==3?.075f:.055f) || Ellipse(x,y,.21f,.13f,.04f,id==3?.075f:.055f);
            bool mouth = false;
            if (id == 0) mouth = Line(x,y,new Vector2(-.13f,-.16f),new Vector2(.13f,-.16f),.018f);
            if (id == 1) mouth = Mathf.Abs(y - (-.24f + 2.5f*x*x)) < .018f && Mathf.Abs(x)<.2f;
            if (id == 2)
            {
                mouth = Mathf.Abs(y - (-.12f - 2*x*x)) < .018f && Mathf.Abs(x)<.16f;
                eye |= Line(x,y,new Vector2(-.29f,.27f),new Vector2(-.12f,.19f),.018f) || Line(x,y,new Vector2(.29f,.27f),new Vector2(.12f,.19f),.018f);
            }
            if (id == 3) mouth = Ellipse(x,y,0,-.17f,.085f,.105f) && !Ellipse(x,y,0,-.17f,.052f,.073f);
            return eye || mouth ? new Color(.035f,.025f,.03f,1) : Color.clear;
        }
        static bool Ellipse(float x,float y,float cx,float cy,float rx,float ry) => (x-cx)*(x-cx)/(rx*rx)+(y-cy)*(y-cy)/(ry*ry)<=1;
        static bool Line(float x,float y,Vector2 a,Vector2 b,float width)
        { var p=new Vector2(x,y);var delta=b-a;return Vector2.Distance(p,a+delta*Mathf.Clamp01(Vector2.Dot(p-a,delta)/delta.sqrMagnitude))<width; }
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
            var row=Rect(footer,"Face Expression",new Vector2(245,32),Vector2.zero);var layout=row.gameObject.AddComponent<LayoutElement>();layout.preferredWidth=245;layout.preferredHeight=32;
            var view=row.gameObject.AddComponent<LobbyExpressionView>();var data=new SerializedObject(view);Bind(data,"lobby",lobby);
            foreach(bool previous in new[]{true,false})
            {
                var r=Rect(row,previous?"Previous":"Next",new Vector2(30,30),new Vector2(previous?-107:107,0));var image=r.gameObject.AddComponent<Image>();image.color=new Color(.12f,.25f,.32f);var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;
                Label(r,"Arrow",previous?"<":">",new Vector2(28,28),Vector2.zero);Bind(data,previous?"previous":"next",button);
            }
            var previewBackground=Rect(row,"Face Preview Background",new Vector2(34,32),new Vector2(-65,0)).gameObject.AddComponent<Image>();previewBackground.color=new Color(.82f,.86f,.9f);previewBackground.raycastTarget=false;
            var preview=Rect(row,"Face Preview",new Vector2(32,32),new Vector2(-65,0)).gameObject.AddComponent<Image>();preview.color=Color.white;preview.raycastTarget=false;Bind(data,"preview",preview);
            Bind(data,"title",Label(row,"Face Name","Neutral",new Vector2(125,30),new Vector2(17,0),16));data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
