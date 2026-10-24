using System;
using System.Text;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class CeremonyStatisticsView : MonoBehaviour
    {
        [SerializeField] private CeremonyStatisticsBindings bindings;
        [SerializeField, Min(.1f)] private float revealSeconds = 3f;
        [SerializeField] private Color selectedTabColor = new Color(.12f, .34f, .62f, 1);
        [SerializeField] private Color idleTabColor = new Color(.07f, .1f, .16f, 1);
        [SerializeField] private Vector2 standardChartHorizontalAnchors = new Vector2(.10f, .855f);
        [SerializeField] private Vector2 combatChartHorizontalAnchors = new Vector2(.25f, .875f);
        [SerializeField] private float combatPlayerLabelOffset = -240f;
        [SerializeField] private float combatMetricLabelOffset = -78f;
        [SerializeField] private float combatAxisExtraSpace = 30f;
        private readonly bool[] _seen = new bool[5];
        private MatchStatistics _data;
        private NetworkMatchState _match;
        private bool _open, _earned, _wired, _preview;
        private int _tab, _hoverSlot = -1, _hoverGroup = -1;
        private float _revealStart, _requestAt;
        public bool IsOpen => _open;
        public CeremonyStatisticsBindings Bindings => bindings;

        public void Configure(CeremonyStatisticsBindings value) => bindings = value;
        private void Awake()
        {
            if (bindings == null) bindings = GetComponent<CeremonyStatisticsBindings>();
            Wire(); Close(); bindings.RootCanvas.enabled = false; bindings.RootRaycaster.enabled = false;
        }
        private void OnEnable() { Wire(); GameText.LanguageChanged += RefreshLanguage; }
        private void OnDisable()
        {
            GameText.LanguageChanged -= RefreshLanguage;
            Close();
            if (!_wired) return;
            bindings.OpenButton.onClick.RemoveListener(Open);
            bindings.CloseButton.onClick.RemoveListener(Close);
            bindings.GoldOption.onClick.RemoveListener(ToggleGold);
            bindings.Tabs[0].onClick.RemoveListener(Results);
            bindings.Tabs[1].onClick.RemoveListener(Gold);
            bindings.Tabs[2].onClick.RemoveListener(Minigames);
            bindings.Tabs[3].onClick.RemoveListener(Combat);
            bindings.Chart.HoverBar -= ShowTooltip;
            _wired = false;
        }
        private void Wire()
        {
            if (_wired || bindings == null || !bindings.HasRequiredReferences) return;
            bindings.OpenButton.onClick.AddListener(Open);
            bindings.CloseButton.onClick.AddListener(Close);
            bindings.GoldOption.onClick.AddListener(ToggleGold);
            bindings.Tabs[0].onClick.AddListener(Results);
            bindings.Tabs[1].onClick.AddListener(Gold);
            bindings.Tabs[2].onClick.AddListener(Minigames);
            bindings.Tabs[3].onClick.AddListener(Combat);
            bindings.Chart.HoverBar += ShowTooltip;
            _wired = true;
            RefreshLanguage();
        }
        private void Results() => SelectTab(0);
        private void Gold() => SelectTab(1);
        private void Minigames() => SelectTab(2);
        private void Combat() => SelectTab(3);
        private void ToggleGold() { _earned = !_earned; SelectTab(1); }

        private void Update()
        {
            if (bindings == null || !bindings.HasRequiredReferences) return;
            if (!_preview)
            {
                var match = NetworkMatchState.Instance;
                if (match != _match)
                {
                    Close(); _match = match; _data = null; Array.Clear(_seen, 0, _seen.Length);
                }
                var playerObject = NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null
                    ? NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject() : null;
                var avatar = playerObject != null ? playerObject.GetComponent<NetworkPlayerAvatar>() : null;
                var available = match != null && match.IsSpawned && match.IsFinalRankingLocked && avatar != null &&
                    !match.IsBackInWaitingRoomDuringCeremony(avatar.AssignedSlot);
                bindings.RootCanvas.enabled = available;
                bindings.RootRaycaster.enabled = available;
                if (!available) { Close(); _data = null; Array.Clear(_seen, 0, _seen.Length); return; }
                if (_open)
                {
                    bindings.Countdown.text = match.IsSimulationSuspended ? GameText.T("CEREMONY PAUSED") :
                        GameText.F("Return to lobby in {0}s", Mathf.CeilToInt((float)match.CeremonyAutoReturnRemaining));
                    if (_data == null && match.CeremonyStatistics != null) SetData(match.CeremonyStatistics);
                    if (_data == null && Time.unscaledTime >= _requestAt)
                    { match.RequestCeremonyStatistics(); _requestAt = Time.unscaledTime + 4f; }
                }
            }
            if (!_open || _data == null) return;
            var progress = Mathf.Clamp01((Time.unscaledTime - _revealStart) / revealSeconds);
            // Linear time sweep makes each change appear in match order.
            bindings.Chart.Reveal(progress);
            UpdateMarkers();
            var mouse = Mouse.current;
            if (bindings.TooltipRoot.activeSelf && mouse != null &&
                !RectTransformUtility.RectangleContainsScreenPoint(bindings.Chart.rectTransform, mouse.position.ReadValue()) &&
                !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)bindings.TooltipRoot.transform, mouse.position.ReadValue())) HideTooltip();
        }

        public void Open()
        {
            if (bindings == null || !bindings.HasRequiredReferences || (!bindings.RootCanvas.enabled && !_preview)) return;
            _open = true; bindings.PopupRoot.SetActive(true);
            UiPopupStack.Push(bindings.PopupRoot, Close);
            if (_match != null && _match.CeremonyStatistics != null && _data != _match.CeremonyStatistics) SetData(_match.CeremonyStatistics);
            else SelectTab(_tab);
            _requestAt = 0;
        }
        public void Close()
        {
            _open = false;
            if (bindings == null) return;
            if (bindings.PopupRoot != null) { UiPopupStack.Remove(bindings.PopupRoot); bindings.PopupRoot.SetActive(false); }
            HideTooltip();
        }
        public void SetData(MatchStatistics data)
        {
            _data = data;
            bindings.Portraits.Render(data);
            for (var i = 0; i < 4; i++) bindings.Heads[i].texture = bindings.Portraits.Get(i);
            SelectTab(_tab);
        }
        public void SelectTab(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, 3); HideTooltip();
            var horizontal = _tab == 3 ? combatChartHorizontalAnchors : standardChartHorizontalAnchors;
            var chartRect = bindings.Chart.rectTransform;
            chartRect.anchorMin = new Vector2(horizontal.x, chartRect.anchorMin.y);
            chartRect.anchorMax = new Vector2(horizontal.y, chartRect.anchorMax.y);
            chartRect.offsetMin = new Vector2(chartRect.offsetMin.x, _tab == 3 ? combatAxisExtraSpace : 0);
            for (var i = 0; i < 4; i++) bindings.Tabs[i].targetGraphic.color = i == _tab ? selectedTabColor : idleTabColor;
            bindings.GoldOption.gameObject.SetActive(_tab == 1);
            bindings.GoldOptionText.text = GameText.T(_earned ? "Show gold balance" : "Show total earned");
            bindings.Chart.Present(_data, _tab, _earned);
            var index = _tab == 1 && _earned ? 4 : _tab;
            _revealStart = Time.unscaledTime - (_seen[index] ? revealSeconds : 0);
            if (_data != null) _seen[index] = true;
            bindings.Chart.Reveal(_seen[index] && Time.unscaledTime - _revealStart >= revealSeconds ? 1 : 0);
            RefreshCopy(); UpdateAxes(); UpdateMarkers();
        }
        private void RefreshLanguage()
        {
            if (bindings == null || !bindings.HasRequiredReferences) return;
            var hoverSlot = _hoverSlot; var hoverGroup = _hoverGroup;
            bindings.Title.text = GameText.T("Match statistics");
            bindings.OpenButtonText.text = GameText.T("Statistics");
            var keys = new[] { "Results", "Gold", "Minigames", "Items & combat" };
            for (var i = 0; i < 4; i++) bindings.TabLabels[i].text = GameText.T(keys[i]);
            if (_open)
            {
                SelectTab(_tab);
                if (hoverSlot >= 0 && hoverGroup >= 0) ShowTooltip(hoverSlot, hoverGroup);
            }
        }
        private static string SafeName(string name, int slot) => string.IsNullOrWhiteSpace(name) ? GameText.F("Player {0}", slot + 1) : name.Replace("<", "＜").Replace(">", "＞");
        private void RefreshCopy()
        {
            var titles = new[] { "Keys held · changes within each turn", _earned ? "Total gold earned · excludes starting gold" : "Gold held · changes within each turn", "Minigame finishing places", "Damage & movement · grouped by player" };
            bindings.AxisTitle.text = GameText.T(titles[_tab]);
            bindings.Description.text = _data == null ? GameText.T("Loading match records…") :
                GameText.T(_data.historyIncomplete ? "Some history is unavailable in this save. Only recorded data is shown." : _tab < 2 ? "Horizontal axis: turns. Changes within a turn appear in event order." : _tab == 2 ? "Tied players each count toward their shared finishing place." : "Hover a bar for details. Movement counts completed board steps.");
            for (var slot = 0; slot < 4; slot++)
            {
                bindings.PlayerNames[slot].text = _data == null ? GameText.F("Player {0}", slot + 1) : SafeName(_data.players[slot].name, slot);
                bindings.PlayerNames[slot].color = _data == null ? Color.white : _data.players[slot].color;
                if (_data == null) { bindings.Summaries[slot].text = "—"; continue; }
                var p = _data.players[slot];
                switch (_tab)
                {
                    case 0:
                        var bonus = new StringBuilder();
                        for (var i = 0; i < 2; i++) if ((p.bonusMask & (1 << i)) != 0)
                        {
                            if (bonus.Length > 0) bonus.Append(" / ");
                            bonus.Append(GameText.T(MatchAwardRules.GetDisplayName((MatchAwardCategory)_data.bonusCategories[i])));
                        }
                        bindings.Summaries[slot].text = GameText.F("Rank {0}  |  Keys {1}  |  Gold {2}", p.rank, p.keys, p.gold) + "\n" +
                            GameText.F("Bonus ({0}): {1}", (p.bonusMask & 1) + ((p.bonusMask >> 1) & 1), bonus.Length == 0 ? GameText.T("None") : bonus.ToString());
                        break;
                    case 1: bindings.Summaries[slot].text = GameText.F("Final balance {0}\nPeak {1}  |  Total earned {2}", p.gold, p.peakGold, p.earnedGold); break;
                    case 2: bindings.Summaries[slot].text = GameText.F("1st {0}  ·  2nd {1}\n3rd {2}  ·  4th {3}", p.wins, _data.placements[slot * 4 + 1], _data.placements[slot * 4 + 2], p.lastPlaces); break;
                    default: bindings.Summaries[slot].text = GameText.F("Items used {0}\nDealt {1}  ·  Received {2}\nMoved {3} tiles", p.itemUses, p.dealt, p.received, p.moves); break;
                }
            }
        }
        private void UpdateAxes()
        {
            foreach (var label in bindings.XLabels) label.gameObject.SetActive(false);
            foreach (var label in bindings.YLabels) label.gameObject.SetActive(false);
            foreach (var label in bindings.CombatNames) label.gameObject.SetActive(false);
            foreach (var label in bindings.CombatTitles) label.gameObject.SetActive(false);
            foreach (var label in bindings.CombatPlayerNames) label.gameObject.SetActive(false);
            if (_data == null) return;
            if (_tab < 3)
                for (var i = 0; i < 5; i++)
                {
                    var label = bindings.YLabels[i]; label.gameObject.SetActive(true);
                    label.text = (bindings.Chart.Maximum * i / 4f).ToString("0.#");
                    At(label.rectTransform, new Vector2(0, i / 4f), new Vector2(-44, 0));
                }
            if (_tab < 2)
                for (var i = 0; i <= _data.turns; i++)
                {
                    var label = bindings.XLabels[i]; label.gameObject.SetActive(true);
                    label.text = i == 0 ? GameText.T("Start") : i.ToString();
                    At(label.rectTransform, new Vector2((float)i / _data.turns, 0), new Vector2(0, -22));
                }
            else if (_tab == 2)
                for (var i = 0; i < 4; i++)
                {
                    var label = bindings.XLabels[i]; label.gameObject.SetActive(true);
                    label.text = GameText.F("Place {0}", i + 1);
                    At(label.rectTransform, new Vector2((i + .45f) / 4f, 0), new Vector2(0, -25));
                }
            else
            {
                // Damage and board steps have different units; both scales start at zero on the left.
                for (var axis = 0; axis < 2; axis++)
                {
                    var title = bindings.CombatTitles[axis]; title.gameObject.SetActive(true);
                    title.text = GameText.T(axis == 0 ? "Damage" : "Tiles moved");
                    At(title.rectTransform, Vector2.zero, new Vector2(-130, -21 - axis * 25));
                    for (var i = 0; i < 5; i++)
                    {
                        var label = bindings.XLabels[axis * 5 + i]; label.gameObject.SetActive(true);
                        label.text = ((axis == 0 ? bindings.Chart.Maximum : bindings.Chart.MovementMaximum) * i / 4f).ToString("0.#");
                        At(label.rectTransform, new Vector2(i / 4f, 0), new Vector2(0, -21 - axis * 25));
                    }
                }
                for (var slot = 0; slot < 4; slot++)
                {
                    var name = bindings.CombatPlayerNames[slot]; name.gameObject.SetActive(true);
                    name.text = SafeName(_data.players[slot].name, slot);
                    name.color = _data.players[slot].color;
                    At(name.rectTransform, new Vector2(0, bindings.Chart.CombatY(1, slot)), new Vector2(combatPlayerLabelOffset, 35));
                }
                for (var group = 0; group < 3; group++)
                {
                    for (var slot = 0; slot < 4; slot++)
                    {
                        var label = bindings.CombatNames[group * 4 + slot]; label.gameObject.SetActive(true);
                        label.text = GameText.T(group == 0 ? "Damage dealt" : group == 1 ? "Damage received" : "Tiles moved");
                        At(label.rectTransform, new Vector2(0, bindings.Chart.CombatY(group, slot)), new Vector2(combatMetricLabelOffset, 0));
                    }
                }
            }
        }
        private void UpdateMarkers()
        {
            for (var slot = 0; slot < 4; slot++)
            {
                var head = bindings.Heads[slot];
                var show = _data != null && (_tab == 3 || bindings.Chart.TryGetHeadPoint(slot, out _));
                head.gameObject.SetActive(show);
                if (show && _tab == 3) At(head.rectTransform, new Vector2(0, bindings.Chart.CombatY(1, slot)), new Vector2(combatPlayerLabelOffset, -6));
                else if (show && bindings.Chart.TryGetHeadPoint(slot, out var point))
                {
                    // Use the same revealed endpoint as the line, including while it rises or falls.
                    At(head.rectTransform, point, Vector2.zero);
                }
            }
            for (var i = 0; i < 16; i++)
            {
                var label = bindings.BarValues[i];
                var show = _data != null && (_tab == 2 || (_tab == 3 && i < 12));
                label.gameObject.SetActive(show);
                if (!show) continue;
                var p = _data.players[i % 4];
                var value = _tab == 2 ? _data.placements[i % 4 * 4 + i / 4] : i < 4 ? p.dealt : i < 8 ? p.received : p.moves;
                label.text = value.ToString();
                At(label.rectTransform, bindings.Chart.ValuePosition(i), _tab == 2 ? new Vector2(0, 17) : new Vector2(28, 0));
            }
        }
        private static void At(RectTransform rt, Vector2 anchor, Vector2 offset)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.anchoredPosition = offset;
        }
        private void HideTooltip()
        {
            _hoverSlot = _hoverGroup = -1;
            if (bindings != null && bindings.TooltipRoot != null) bindings.TooltipRoot.SetActive(false);
        }
        private void ShowTooltip(int slot, int group)
        {
            if (_data == null || (_hoverSlot == slot && _hoverGroup == group)) return;
            _hoverSlot = slot; _hoverGroup = group;
            bindings.TooltipTitle.text = SafeName(_data.players[slot].name, slot) + " · " +
                GameText.T(group == 0 ? "Damage dealt" : group == 1 ? "Damage received" : "Tiles moved");
            var lines = new StringBuilder();
            if (group == 2) lines.Append(GameText.F("Completed board steps: {0}\nSwaps and teleports are excluded.", _data.players[slot].moves));
            else foreach (var e in _data.damage)
            {
                if (group == 0 ? e.source != slot || e.target == slot : e.target != slot) continue;
                var other = group == 0 ? e.target : e.source;
                var name = other < 0 ? GameText.T("Environment / self") : SafeName(_data.players[other].name, other);
                var item = e.item == -1 ? GameText.T("Punch") : e.item == 0 ? GameText.T("Environment / self") : GameText.T(PrototypeItemCatalog.Get((PrototypeItemId)e.item).DisplayName);
                lines.Append(GameText.F("Turn {0} | {1} | -{2} | {3}", e.turn, item, e.amount, name)).Append('\n');
            }
            if (lines.Length == 0) lines.Append(GameText.T("No detailed records"));
            bindings.TooltipText.text = lines.ToString();
            bindings.TooltipRoot.SetActive(true);
            Canvas.ForceUpdateCanvases();
            bindings.TooltipScroll.verticalNormalizedPosition = 1;
        }
#if UNITY_EDITOR
        public void Preview(MatchStatistics data, int tab, bool earned = false)
        {
            _preview = true; bindings.RootCanvas.enabled = true; bindings.RootRaycaster.enabled = true; _earned = earned;
            _open = true; bindings.PopupRoot.SetActive(true); _tab = tab; SetData(data);
            bindings.Countdown.text = GameText.F("Return to lobby in {0}s", 120);
        }
#endif
    }
}
