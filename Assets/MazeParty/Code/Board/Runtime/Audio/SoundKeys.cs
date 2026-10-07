using System;
using System.Collections.Generic;
using System.Text;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Every sound key the game plays. Code refers to these constants; each key
    /// has one <see cref="SoundCue"/> in the sound library (created by
    /// MazeParty/Audio/Install Sound System). The prefix decides the channel:
    /// <c>bgm.</c> music, <c>ui.</c> interface, anything else effects.
    /// </summary>
    public static class SoundKeys
    {
        public const string BgmPrefix = "bgm.";
        public const string UiPrefix = "ui.";
        public const string MinigameBgmPrefix = "bgm.minigame.";
        public const string ItemUsePrefix = "item.use.";

        // Music. Per-minigame tracks: MinigameBgm(id), falling back to BgmMinigame.
        public const string BgmLobby = "bgm.lobby";
        public const string BgmWaitingRoom = "bgm.waiting_room";
        public const string BgmBoard = "bgm.board";
        public const string BgmMinigame = "bgm.minigame";
        public const string BgmCeremony = "bgm.ceremony";

        // Interface.
        public const string UiHover = "ui.hover";
        public const string UiClick = "ui.click";
        public const string UiBack = "ui.back";
        public const string UiConfirm = "ui.confirm";
        public const string UiError = "ui.error";
        public const string UiPopupOpen = "ui.popup_open";
        public const string UiNotice = "ui.notice";

        // Combat (hits come in variations: add clips to the cue).
        public const string CombatPunchSwing = "combat.punch.swing";
        public const string CombatHitBody = "combat.hit.body";
        public const string CombatHitHead = "combat.hit.head";
        public const string CombatHitHand = "combat.hit.hand";
        public const string CombatKnockOut = "combat.ko";
        public const string CombatRespawn = "combat.respawn";

        // Board.
        public const string BoardTurnStart = "board.turn_start";
        public const string BoardDiceRoll = "board.dice.roll";
        public const string BoardDiceBounce = "board.dice.bounce";
        public const string BoardDiceResult = "board.dice.result";
        public const string BoardGoldGain = "board.gold.gain";
        public const string BoardGoldLoss = "board.gold.loss";
        public const string BoardKeyBuy = "board.key.buy";
        public const string BoardShopBuy = "board.shop.buy";
        public const string BoardShopFail = "board.shop.fail";
        public const string BoardFootstep = "board.footstep";

        // Items. Per-item use sounds: ItemUse(id).
        public const string ItemExplosion = "item.explosion";
        public const string ItemBulletImpact = "item.bullet_impact";

        // Shared minigame flow.
        public const string MinigameReveal = "minigame.reveal";
        public const string MinigameCountdownTick = "minigame.countdown.tick";
        public const string MinigameCountdownGo = "minigame.countdown.go";
        public const string MinigameFinish = "minigame.finish";
        public const string MinigameSequenceMemoryTone =
            "minigame.sequence_memory.tone";

        // Award ceremony.
        public const string CeremonyAwardReady = "ceremony.award_ready";
        public const string CeremonyAward = "ceremony.award";
        public const string CeremonyFanfare = "ceremony.fanfare";
        public const string CeremonyApplause = "ceremony.applause";

        private static readonly string[] FixedKeys =
        {
            BgmLobby, BgmWaitingRoom, BgmBoard, BgmMinigame, BgmCeremony,
            UiHover, UiClick, UiBack, UiConfirm, UiError, UiPopupOpen, UiNotice,
            CombatPunchSwing, CombatHitBody, CombatHitHead, CombatHitHand,
            CombatKnockOut, CombatRespawn,
            BoardTurnStart, BoardDiceRoll, BoardDiceBounce, BoardDiceResult,
            BoardGoldGain, BoardGoldLoss, BoardKeyBuy, BoardShopBuy, BoardShopFail,
            BoardFootstep,
            ItemExplosion, ItemBulletImpact,
            MinigameReveal, MinigameCountdownTick, MinigameCountdownGo,
            MinigameFinish, MinigameSequenceMemoryTone,
            CeremonyAwardReady, CeremonyAward, CeremonyFanfare, CeremonyApplause
        };

        /// <summary>Music of one minigame, e.g. <c>bgm.minigame.arena_combat</c>.</summary>
        public static string MinigameBgm(ScheduledMinigameId minigame)
        {
            return MinigameBgmPrefix + ToSnakeCase(minigame.ToString());
        }

        /// <summary>Use sound of one board item, e.g. <c>item.use.position_swapper</c>.</summary>
        public static string ItemUse(PrototypeItemId item)
        {
            return ItemUsePrefix + ToSnakeCase(item.ToString());
        }

        public static string CombatHit(PlayerHitRegion region)
        {
            switch (region)
            {
                case PlayerHitRegion.Head:
                    return CombatHitHead;
                case PlayerHitRegion.Hand:
                    return CombatHitHand;
                default:
                    return CombatHitBody;
            }
        }

        public static AudioChannel GetChannel(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                if (key.StartsWith(BgmPrefix, StringComparison.Ordinal))
                {
                    return AudioChannel.Bgm;
                }

                if (key.StartsWith(UiPrefix, StringComparison.Ordinal))
                {
                    return AudioChannel.Ui;
                }
            }

            return AudioChannel.Sfx;
        }

        /// <summary>
        /// Every key the game can play: the fixed keys plus one music key per
        /// registered minigame and one use key per board item.
        /// </summary>
        public static IReadOnlyList<string> All()
        {
            var keys = new List<string>(FixedKeys);
            foreach (var definition in MinigameCatalog.RegisteredMinigames)
            {
                keys.Add(MinigameBgm(definition.Id));
            }

            foreach (PrototypeItemId item in Enum.GetValues(typeof(PrototypeItemId)))
            {
                if (item != PrototypeItemId.None)
                {
                    keys.Add(ItemUse(item));
                }
            }

            return keys;
        }

        /// <summary>"PositionSwapper" → "position_swapper".</summary>
        public static string ToSnakeCase(string pascal)
        {
            if (string.IsNullOrEmpty(pascal))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(pascal.Length + 8);
            for (var index = 0; index < pascal.Length; index++)
            {
                var character = pascal[index];
                if (char.IsUpper(character))
                {
                    if (index > 0)
                    {
                        builder.Append('_');
                    }

                    builder.Append(char.ToLowerInvariant(character));
                }
                else
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }
}
