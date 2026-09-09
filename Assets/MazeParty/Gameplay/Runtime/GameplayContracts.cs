using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum GameplayMode
    {
        FirstPerson,
        BoardTopDown,
        CombatSpectator,
        Minigame
    }

    public enum DamageKind
    {
        Item,
        Environment
    }

    public enum DamageResult
    {
        Ignored,
        Blocked,
        Applied
    }

    public readonly struct DamageRequest
    {
        public DamageRequest(
            int amount,
            DamageKind kind,
            GameObject source,
            PlayerHitRegion hitRegion = PlayerHitRegion.Body)
        {
            Amount = Mathf.Max(0, amount);
            Kind = kind;
            Source = source;
            HitRegion = hitRegion;
        }

        public int Amount { get; }
        public DamageKind Kind { get; }
        public GameObject Source { get; }
        public PlayerHitRegion HitRegion { get; }
    }

    public readonly struct GameplayHitReport
    {
        public GameplayHitReport(DamageResult damageResult, bool pushApplied)
        {
            DamageResult = damageResult;
            PushApplied = pushApplied;
        }

        public DamageResult DamageResult { get; }
        public bool PushApplied { get; }
    }

    public interface IGameplayInputSource
    {
        Vector2 Move { get; }
        Vector2 Look { get; }
        bool WalkHeld { get; }
        bool PrimaryPressed { get; }
        bool PrimaryHeld { get; }
        bool SecondaryPressed { get; }
        bool CancelPressed { get; }
    }

    public interface IInteractable
    {
        string InteractionPrompt { get; }
        void Interact(GameObject instigator);
    }

    public interface IDamageable
    {
        int CurrentHealth { get; }
        DamageResult ApplyDamage(DamageRequest request);
    }

    public interface IPushReceiver
    {
        void ApplyPush(Vector3 impulse);
    }

    public interface IGameplayCameraService
    {
        GameplayMode ActiveMode { get; }
        Camera OutputCamera { get; }
        void SwitchTo(GameplayMode mode);
        void SetUiPointerVisible(bool visible);
    }
}
