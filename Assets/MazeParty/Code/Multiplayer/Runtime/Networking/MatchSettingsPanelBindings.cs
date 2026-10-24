using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class MatchSettingsPanelBindings : MonoBehaviour
    {
        [SerializeField] private Text turns, selectedCount;
        [SerializeField] private Button lessTurns, moreTurns;
        [SerializeField] private ScrollRect list;
        [SerializeField] private MatchMinigameRowBindings[] rows;
        private MatchGameSettings _settings;
        private Action<MatchGameSettings> _changed;
        public bool HasRequiredReferences => turns && selectedCount && lessTurns && moreTurns && list &&
            list.content && list.viewport && rows != null && rows.Length == MatchGameSettings.GameCount &&
            Array.TrueForAll(rows,r=>r && r.HasRequiredReferences);
        public void Configure(Text turnText,Text countText,Button previous,Button next,ScrollRect scroll,MatchMinigameRowBindings[] entries)
        { turns=turnText;selectedCount=countText;lessTurns=previous;moreTurns=next;list=scroll;rows=entries; }
        private void Awake()
        {
            if(!HasRequiredReferences){enabled=false;return;}
            lessTurns.onClick.AddListener(()=>ChangeTurns(-5));
            moreTurns.onClick.AddListener(()=>ChangeTurns(5));
        }
        private void Update()
        {
            var source=OnlineSessionController.Instance;
            if(source==null)return;
            var snapshot=source.CurrentSession;
            var editable=snapshot!=null && SessionRules.CanChangeBoardMap(snapshot.IsHost,snapshot.Phase) &&
                !source.IsBusy && !source.IsBoardMapSelectionLocked;
            Present(snapshot?.GameSettings,editable,source.RequestGameSettings);
        }
        public void Present(MatchGameSettings settings,bool editable,Action<MatchGameSettings> changed)
        {
            _settings=settings;_changed=changed;
            if(settings==null)
            {
                selectedCount.text=GameText.T("Invalid match settings.");
                lessTurns.interactable=false;moreTurns.interactable=false;
                foreach(var row in rows)row.Present(MatchGameSettings.Default,false,null);
                return;
            }
            turns.text=GameText.F("{0} turns",settings.Turns);
            selectedCount.text=GameText.F("Selected {0} / {1} required",settings.IncludedCount,settings.Turns);
            lessTurns.interactable=editable && settings.TryChangeTurns(settings.Turns-5,out _);
            moreTurns.interactable=editable && settings.TryChangeTurns(settings.Turns+5,out _);
            foreach(var row in rows)row.Present(settings,editable,changed);
        }
        private void ChangeTurns(int delta)
        {
            if(_settings!=null && _settings.TryChangeTurns(_settings.Turns+delta,out var next))_changed?.Invoke(next);
        }
    }
}
