using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed class GameplayHealth : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1)] private int maxHealth = 100;

        private GameplayPhaseClock _phaseClock;
        private Func<double> _timeProvider;
        private bool _initialized;

        public event Action<int, int> HealthChanged;
        public event Action<DamageRequest> DamageBlocked;

        public int CurrentHealth { get; private set; }
        public int MaxHealth => maxHealth;
        public bool IsAlive => CurrentHealth > 0;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void ConfigureProtection(GameplayPhaseClock phaseClock, Func<double> timeProvider = null)
        {
            EnsureInitialized();
            _phaseClock = phaseClock;
            _timeProvider = timeProvider;
        }

        public DamageResult ApplyDamage(DamageRequest request)
        {
            EnsureInitialized();
            if (request.Amount <= 0 || !IsAlive)
                return DamageResult.Ignored;

            var now = _timeProvider != null ? _timeProvider() : Time.timeAsDouble;
            if (request.Kind == DamageKind.Item
                && _phaseClock != null
                && _phaseClock.IsOpeningProtectionActive(now))
            {
                DamageBlocked?.Invoke(request);
                return DamageResult.Blocked;
            }

            CurrentHealth = Mathf.Max(0, CurrentHealth - request.Amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            return DamageResult.Applied;
        }

        public void Heal(int amount)
        {
            EnsureInitialized();
            if (amount <= 0 || !IsAlive)
                return;

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            _initialized = true;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            CurrentHealth = maxHealth;
            _initialized = true;
        }

    }

    /// <summary>
    /// Resolves damage and push through separate interfaces. Protection may block
    /// item HP damage, but never cancels hit feedback or physical displacement.
    /// </summary>
    public static class GameplayHitResolver
    {
        public static GameplayHitReport Resolve(
            GameObject target,
            DamageRequest damage,
            Vector3 pushImpulse)
        {
            if (target == null)
                return new GameplayHitReport(DamageResult.Ignored, false);

            var damageable = FindInParents<IDamageable>(target);
            var pushReceiver = FindInParents<IPushReceiver>(target);

            var damageResult = damageable != null
                ? damageable.ApplyDamage(damage)
                : DamageResult.Ignored;

            var pushApplied = pushReceiver != null && pushImpulse.sqrMagnitude > 0f;
            if (pushApplied)
                pushReceiver.ApplyPush(pushImpulse);

            return new GameplayHitReport(damageResult, pushApplied);
        }

        private static T FindInParents<T>(GameObject target) where T : class
        {
            var current = target.transform;
            while (current != null)
            {
                var behaviours = current.GetComponents<MonoBehaviour>();
                for (var i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is T match)
                        return match;
                }

                current = current.parent;
            }

            return null;
        }
    }
}
