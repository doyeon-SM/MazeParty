using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    // Stable one-based IDs shared by STOVE and Steam. Never renumber.
    public enum PartyAchievement
    {
        FirstCeremony = 1, RegularGuest, Champion, FiveCrowns, ForestExplorer, MazeExplorer,
        FirstKey, KeyRegular, MovingShop, FourKeys, BonusWinner, FirstItem, ItemRegular,
        BoardWinner, ThreePunches, Swap, InvisibleCustomer, Sniper, Pistol, MineKill,
        OwnMine, GrenadeKill, OwnGrenade, TwentyFourSteps, FirstMinigameWin,
        FiveMinigameWins, WinStreak, AllMinigames, PerfectMemory, PerfectStairs,
        PerfectMinefield, PerfectBalloon, CatchEveryone, TenStolenGifts
    }

    public static class PartyAchievements
    {
        public const int Count = 34;
        public const long All = (1L << Count) - 1;
        public static long Bit(PartyAchievement id) => 1L << ((int)id - 1);
        // Achievement numbers remain stable; only canonical stat numbers enter the outbox.
        public static int StatNumber(int number)
        {
            if (number < 1 || number > Count) throw new ArgumentOutOfRangeException(nameof(number));
            switch ((PartyAchievement)number)
            {
                case PartyAchievement.RegularGuest: return (int)PartyAchievement.FirstCeremony;
                case PartyAchievement.FiveCrowns: return (int)PartyAchievement.Champion;
                case PartyAchievement.KeyRegular: return (int)PartyAchievement.FirstKey;
                case PartyAchievement.ItemRegular: return (int)PartyAchievement.FirstItem;
                case PartyAchievement.FiveMinigameWins: return (int)PartyAchievement.FirstMinigameWin;
                default: return number;
            }
        }
        public static bool IsSharedStat(int number)
        {
            var canonical = StatNumber(number);
            return canonical == 1 || canonical == 3 || canonical == 7 || canonical == 12 || canonical == 25;
        }
        public static string StatId(int number) => "MP_ACH_" + StatNumber(number).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        public static int Target(int number)
        {
            _ = StatNumber(number);
            switch ((PartyAchievement)number)
            {
                case PartyAchievement.RegularGuest:
                case PartyAchievement.FiveCrowns:
                case PartyAchievement.FiveMinigameWins: return 5;
                case PartyAchievement.KeyRegular:
                case PartyAchievement.ItemRegular: return 10;
                default: return 1;
            }
        }
        public static string ApiName(int number) => StatId(number) + "_" + Target(number).ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool EligibleTurns(int turns) => turns == 10 || turns == 15 || turns == 20;
        public static int RegisteredGamesMask
        {
            get
            {
                var mask = 0;
                foreach (var game in MinigameCatalog.RegisteredMinigames) mask |= 1 << ((int)game.Id - 1);
                return mask;
            }
        }
    }

    [Serializable]
    public sealed class AchievementMatchProgress
    {
        public int schemaVersion = 1;
        public long flags;
        public int keysBought, itemsBought, stolenGifts, gamesPlayed;
        public int minigameWins, winStreak, lastMinigameTurn;
        public int lastKeyTurn, keysThisTurn;
        public void Earn(PartyAchievement id) => flags |= PartyAchievements.Bit(id);
        public void PurchaseKey(int turn, bool cloaked)
        {
            keysBought++;
            keysThisTurn = lastKeyTurn == turn ? keysThisTurn + 1 : 1;
            lastKeyTurn = turn;
            if (keysThisTurn >= 2) Earn(PartyAchievement.MovingShop);
            if (cloaked) Earn(PartyAchievement.InvisibleCustomer);
        }
        public void CompleteMinigame(int turn, ScheduledMinigameId game, int rank, bool perfectMemory)
        {
            if (turn <= lastMinigameTurn || !MinigameCatalog.IsRegistered(game) || rank < 1 || rank > 4) return;
            lastMinigameTurn = turn;
            gamesPlayed |= 1 << ((int)game - 1);
            if (rank != 1) { winStreak = 0; return; }
            minigameWins++;
            winStreak++;
            Earn(PartyAchievement.FirstMinigameWin);
            if (minigameWins >= 5) Earn(PartyAchievement.FiveMinigameWins);
            if (winStreak >= 3) Earn(PartyAchievement.WinStreak);
            if (game == ScheduledMinigameId.SequenceMemory && perfectMemory) Earn(PartyAchievement.PerfectMemory);
        }
        public void ItemDeath(PrototypeItemId item, bool self)
        {
            switch (item)
            {
                case PrototypeItemId.Sniper: if (!self) Earn(PartyAchievement.Sniper); break;
                case PrototypeItemId.Pistol: if (!self) Earn(PartyAchievement.Pistol); break;
                case PrototypeItemId.Mine: Earn(self ? PartyAchievement.OwnMine : PartyAchievement.MineKill); break;
                case PrototypeItemId.Grenade: Earn(self ? PartyAchievement.OwnGrenade : PartyAchievement.GrenadeKill); break;
            }
        }
        public static bool TryDecode(string json, out AchievementMatchProgress progress)
        {
            progress = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var decoded = UnityEngine.JsonUtility.FromJson<AchievementMatchProgress>(json);
                if (decoded == null || !decoded.IsValid) return false;
                progress = decoded; return true;
            }
            catch (ArgumentException) { return false; }
        }
        public bool IsValid => schemaVersion == 1 && (flags & ~PartyAchievements.All) == 0 && keysBought >= 0 && itemsBought >= 0 &&
            stolenGifts >= 0 && minigameWins >= 0 && minigameWins <= 20 && winStreak >= 0 &&
            winStreak <= minigameWins && lastMinigameTurn >= 0 && lastMinigameTurn <= 20 &&
            lastKeyTurn >= 0 && lastKeyTurn <= 20 && keysThisTurn >= 0 && keysThisTurn <= keysBought &&
            (gamesPlayed & ~PartyAchievements.RegisteredGamesMask) == 0;
    }

    [Serializable]
    public sealed class AchievementReceipt
    {
        public string matchId, playerKey, mapId;
        public int turns, rank, finalKeys;
        public bool bonusWinner;
        public AchievementMatchProgress progress;
        public bool IsValid => Guid.TryParseExact(matchId, "N", out _) && !string.IsNullOrEmpty(playerKey) &&
            PartyAchievements.EligibleTurns(turns) && rank >= 1 && rank <= 4 && finalKeys >= 0 &&
            progress != null && progress.IsValid;
    }
}
