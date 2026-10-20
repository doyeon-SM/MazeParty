using System;
using System.Linq;
using MazeParty.Gameplay.Minigames;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Editor
{
    public static class MatchSettingsProjectSetup
    {
        public const string PanelPath="Assets/MazeParty/Prefabs/Multiplayer/UI/MatchSettingsPanel.prefab";
        public const string RowPath="Assets/MazeParty/Prefabs/Multiplayer/UI/MatchMinigameRow.prefab";
        private const string LobbyPath="Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";
        private const string TowerPath="Assets/MazeParty/Prefabs/Board/UI/MinigameScheduleTower.prefab";
        [MenuItem("MazeParty/Multiplayer/Install Match Settings")]
        public static void Install()
        {
            var lobbyAsset=AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPath);
            var font=lobbyAsset.GetComponentsInChildren<Text>(true).Select(x=>x.font).First(x=>x!=null);
            if(!AssetDatabase.LoadAssetAtPath<GameObject>(RowPath))
            {
                var row=BuildRow(font);
                try{PrefabUtility.SaveAsPrefabAsset(row,RowPath);}finally{UnityEngine.Object.DestroyImmediate(row);}
            }
            if(!AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath))
            {
                var panel=BuildPanel(font);
                try{PrefabUtility.SaveAsPrefabAsset(panel,PanelPath);}finally{UnityEngine.Object.DestroyImmediate(panel);}
            }
            var lobby=PrefabUtility.LoadPrefabContents(LobbyPath);
            try
            {
                if(!lobby.GetComponentInChildren<MatchSettingsPanelBindings>(true))
                {
                    var popup=lobby.GetComponentsInChildren<RectTransform>(true).First(t=>t.name=="Board Settings Popup");
                    popup.sizeDelta=new Vector2(1120,900);
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath),popup);
                    instance.transform.SetSiblingIndex(2);
                    PrefabUtility.SaveAsPrefabAsset(lobby,LobbyPath);
                }
            }
            finally{PrefabUtility.UnloadPrefabContents(lobby);}
            InstallTower();
            AssetDatabase.SaveAssets();
        }
        private static GameObject Ui(string name,Transform parent=null)
        {
            var o=new GameObject(name,typeof(RectTransform));o.layer=LayerMask.NameToLayer("UI");
            if(parent)o.transform.SetParent(parent,false);return o;
        }
        private static void Size(GameObject o,float width,float height)
        {
            o.GetComponent<RectTransform>().sizeDelta=new Vector2(width,height);
            var e=o.GetComponent<LayoutElement>()??o.AddComponent<LayoutElement>();
            e.preferredWidth=width;e.preferredHeight=height;e.minHeight=height;e.flexibleWidth=0;e.flexibleHeight=0;
        }
        private static HorizontalLayoutGroup Horizontal(GameObject o,float spacing=8)
        {
            var group=o.AddComponent<HorizontalLayoutGroup>();group.spacing=spacing;group.childAlignment=TextAnchor.MiddleCenter;
            group.childControlWidth=true;group.childControlHeight=true;group.childForceExpandWidth=false;group.childForceExpandHeight=false;return group;
        }
        private static Text Label(string name,Transform parent,string value,Font font,float width,float height=40,int size=19)
        {
            var o=Ui(name,parent);Size(o,width,height);var text=o.AddComponent<Text>();
            text.font=font;text.fontSize=size;text.fontStyle=FontStyle.Normal;text.color=Color.white;
            text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.text=value;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            return text;
        }
        private static void Localize(Text text,string source){text.gameObject.AddComponent<LocalizedText>().Configure(source);}
        private static Button Button(string name,Transform parent,string label,Font font,float width=38)
        {
            var o=Ui(name,parent);Size(o,width,36);
            var bg=o.AddComponent<Image>();bg.color=new Color(.2f,.17f,.34f,1);
            var button=o.AddComponent<Button>();button.targetGraphic=bg;o.AddComponent<UiSoundEmitter>();
            var colors=button.colors;colors.disabledColor=new Color(.4f,.4f,.4f,.4f);button.colors=colors;
            var text=Label("Label",o.transform,label,font,width,36,21);
            Stretch(text.rectTransform);
            return button;
        }
        private static void Stretch(RectTransform rect)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=Vector2.zero;rect.offsetMax=Vector2.zero;}
        private static ScrollRect Scroll(Transform parent,float width,float height,out RectTransform content,bool layout)
        {
            var o=Ui("Scroll View",parent);Size(o,width,height);
            var scroll=o.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=38;scroll.inertia=true;
            var viewport=Ui("Viewport",o.transform);Stretch(viewport.GetComponent<RectTransform>());
            var image=viewport.AddComponent<Image>();image.color=new Color(.025f,.025f,.06f,.65f);image.raycastTarget=true;
            viewport.AddComponent<RectMask2D>();
            var c=Ui("Content",viewport.transform);content=c.GetComponent<RectTransform>();
            content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.pivot=new Vector2(.5f,1);
            content.anchoredPosition=Vector2.zero;content.sizeDelta=Vector2.zero;
            if(layout)
            {
                var group=c.AddComponent<VerticalLayoutGroup>();group.spacing=0;group.childControlWidth=true;
                group.childControlHeight=true;group.childForceExpandWidth=true;group.childForceExpandHeight=false;
                var fit=c.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            }
            scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=content;
            // Deliberately no scrollbar: wheel and drag operate directly on the viewport.
            return scroll;
        }
        private static GameObject BuildRow(Font font)
        {
            var root=Ui("Minigame Settings Row");Size(root,1030,48);Horizontal(root,12);
            var name=Label("Game",root.transform,"MINIGAME",font,350,44);
            var toggleCell=Ui("Included Cell",root.transform);Size(toggleCell,100,44);
            var toggleObject=Ui("Included",toggleCell.transform);var rt=toggleObject.GetComponent<RectTransform>();
            rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);rt.sizeDelta=new Vector2(28,28);
            var bg=toggleObject.AddComponent<Image>();bg.color=new Color(.28f,.26f,.4f,1);
            var toggle=toggleObject.AddComponent<Toggle>();toggle.targetGraphic=bg;toggleObject.AddComponent<UiSoundEmitter>();
            var mark=Ui("Check",toggleObject.transform);var markRect=mark.GetComponent<RectTransform>();Stretch(markRect);
            markRect.offsetMin=new Vector2(6,6);markRect.offsetMax=new Vector2(-6,-6);
            var check=mark.AddComponent<Image>();check.color=new Color(.45f,.95f,.75f,1);check.raycastTarget=false;
            toggle.graphic=check;toggle.isOn=true;
            var previous=Button("Less Rounds",root.transform,"<",font);
            var rounds=Label("Rounds",root.transform,"3",font,70);
            var next=Button("More Rounds",root.transform,">",font);
            var previousTime=Button("Less Time",root.transform,"<",font);
            var seconds=Label("Seconds",root.transform,"60s",font,130);
            var nextTime=Button("More Time",root.transform,">",font);
            root.AddComponent<MatchMinigameRowBindings>().Configure(ScheduledMinigameId.Minefield,name,toggle,rounds,seconds,
                previous,next,previousTime,nextTime);
            return root;
        }
        private static GameObject BuildPanel(Font font)
        {
            var root=Ui("Match Settings");Size(root,1060,620);
            var layout=root.AddComponent<VerticalLayoutGroup>();layout.spacing=8;
            layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;
            var turnRow=Ui("Turn Count",root.transform);Size(turnRow,1040,44);Horizontal(turnRow,20);
            var title=Label("Title",turnRow.transform,"Map Turns",font,250);Localize(title,"Map Turns");
            var prev=Button("Less Turns",turnRow.transform,"<",font,44);
            var turns=Label("Turns",turnRow.transform,"15 turns",font,130);
            var next=Button("More Turns",turnRow.transform,">",font,44);
            var count=Label("Selected Count",turnRow.transform,"Selected 20 / 15 required",font,410,40,18);
            var header=Ui("Columns",root.transform);Size(header,1040,34);Horizontal(header,12);
            Localize(Label("Name",header.transform,"Minigame",font,350,34,18),"Minigame");
            Localize(Label("Include",header.transform,"Include",font,100,34,18),"Include");
            Localize(Label("Rounds",header.transform,"Rounds",font,170,34,18),"Rounds");
            Localize(Label("Time",header.transform,"Round Time",font,240,34,18),"Round Time");
            var scroll=Scroll(root.transform,1040,480,out var content,true);
            var entries=new MatchMinigameRowBindings[MatchGameSettings.GameCount];
            var rowAsset=AssetDatabase.LoadAssetAtPath<GameObject>(RowPath);
            for(var i=0;i<entries.Length;i++)
            {
                var row=(GameObject)PrefabUtility.InstantiatePrefab(rowAsset,content);row.name="Game "+(i+1);
                var binding=row.GetComponent<MatchMinigameRowBindings>();
                var serialized=new SerializedObject(binding);serialized.FindProperty("game").intValue=i+1;serialized.ApplyModifiedPropertiesWithoutUndo();
                entries[i]=binding;
            }
            var hint=Label("Hint",root.transform,"Select at least as many minigames as map turns.",font,1040,30,16);
            Localize(hint,"Select at least as many minigames as map turns.");
            root.AddComponent<MatchSettingsPanelBindings>().Configure(turns,count,prev,next,scroll,entries);
            return root;
        }
        private static void InstallTower()
        {
            var root=PrefabUtility.LoadPrefabContents(TowerPath);
            try
            {
                var view=root.GetComponent<MinigameScheduleTowerView>();
                var serialized=new SerializedObject(view);
                if(serialized.FindProperty("scrollView").objectReferenceValue!=null)
                {if(!view.HasRequiredReferences)throw new InvalidOperationException("Tower bindings invalid.");return;}
                var panel=(RectTransform)serialized.FindProperty("panel").objectReferenceValue;
                var oldBlocks=serialized.FindProperty("blocks");
                var blocks=new Image[MinigameScheduleTowerView.MaximumVisibleBlocks];
                var labels=new Text[blocks.Length];
                var first=(Image)oldBlocks.GetArrayElementAtIndex(0).objectReferenceValue;
                var scroll=Scroll(panel,280,480,out var content,false);
                var scrollRect=scroll.GetComponent<RectTransform>();scrollRect.anchorMin=scrollRect.anchorMax=new Vector2(.5f,1);
                scrollRect.pivot=new Vector2(.5f,1);scrollRect.anchoredPosition=new Vector2(0,-108);
                for(var i=0;i<blocks.Length;i++)
                {
                    var block=i<oldBlocks.arraySize?(Image)oldBlocks.GetArrayElementAtIndex(i).objectReferenceValue:
                        UnityEngine.Object.Instantiate(first,content);
                    block.name="Block "+(i+1);block.transform.SetParent(content,false);
                    var rect=block.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);
                    rect.anchoredPosition=new Vector2(0,-i*48-4);rect.sizeDelta=new Vector2(264,40);
                    block.raycastTarget=false;blocks[i]=block;labels[i]=block.GetComponentInChildren<Text>(true);
                }
                content.sizeDelta=new Vector2(0,blocks.Length*48);panel.sizeDelta=new Vector2(panel.sizeDelta.x,612);
                view.Configure(root.GetComponent<Canvas>(),panel,
                    (Text)serialized.FindProperty("title").objectReferenceValue,
                    (Text)serialized.FindProperty("subtitle").objectReferenceValue,blocks,labels);
                view.ConfigureScroll(scroll,content,48);
                PrefabUtility.SaveAsPrefabAsset(root,TowerPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
