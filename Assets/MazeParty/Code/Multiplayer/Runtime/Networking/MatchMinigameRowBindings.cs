using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class MatchMinigameRowBindings : MonoBehaviour
    {
        [SerializeField] private ScheduledMinigameId game;
        [SerializeField] private Text gameName, rounds, seconds;
        [SerializeField] private Toggle included;
        [SerializeField] private Button lessRounds, moreRounds, lessTime, moreTime;
        private MatchGameSettings _settings;
        private Action<MatchGameSettings> _changed;
        public bool HasRequiredReferences => gameName && rounds && seconds && included && lessRounds && moreRounds && lessTime && moreTime;
        public ScheduledMinigameId Game => game;
        public void Configure(ScheduledMinigameId id, Text name, Toggle toggle, Text roundText, Text timeText,
            Button previousRound, Button nextRound, Button previousTime, Button nextTime)
        { game=id; gameName=name; included=toggle; rounds=roundText; seconds=timeText;
          lessRounds=previousRound; moreRounds=nextRound; lessTime=previousTime; moreTime=nextTime; }
        private void Awake()
        {
            if (!HasRequiredReferences) { enabled=false; return; }
            included.onValueChanged.AddListener(ChangeIncluded);
            lessRounds.onClick.AddListener(()=>Change(-1,0)); moreRounds.onClick.AddListener(()=>Change(1,0));
            lessTime.onClick.AddListener(()=>Change(0,-15)); moreTime.onClick.AddListener(()=>Change(0,15));
        }
        public void Present(MatchGameSettings settings, bool editable, Action<MatchGameSettings> changed)
        {
            _settings=settings; _changed=editable ? changed : null;
            gameName.text=GameText.T(MinigameCatalog.GetDisplayName(game));
            var value=settings.Get(game);
            included.SetIsOnWithoutNotify(value.Included);
            included.interactable=editable && (!value.Included || settings.IncludedCount>settings.Turns);
            rounds.text=value.Rounds.ToString();
            seconds.text=MatchGameSettings.HasTimeLimit(game) ? GameText.F("{0}s",value.Seconds) : GameText.T("Fixed");
            lessRounds.interactable=editable && value.Rounds>1;
            moreRounds.interactable=editable && value.Rounds<5;
            lessTime.interactable=editable && value.Seconds>30;
            moreTime.interactable=editable && value.Seconds>0 && value.Seconds<MatchGameSettings.MaximumSeconds(game);
        }
        private void ChangeIncluded(bool value)
        {
            if (_settings==null) return;
            var old=_settings.Get(game);
            if(_settings.TryChangeGame(game,new MinigameSetting(value,old.Rounds,old.Seconds),out var next)) _changed?.Invoke(next);
            else included.SetIsOnWithoutNotify(old.Included);
        }
        private void Change(int roundDelta,int timeDelta)
        {
            if(_settings==null)return;
            var old=_settings.Get(game);
            if(_settings.TryChangeGame(game,new MinigameSetting(old.Included,old.Rounds+roundDelta,old.Seconds+timeDelta),out var next))
                _changed?.Invoke(next);
        }
    }
}
