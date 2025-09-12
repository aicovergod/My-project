using UnityEngine;
using Combat;

namespace Status.Poison
{
    /// <summary>
    /// Handles applying and updating poison on an entity.
    /// </summary>
    public class PoisonController : MonoBehaviour
    {
        [Tooltip("Stats component implementing CombatTarget for this entity.")]
        [SerializeField] private MonoBehaviour statsComponent;

        private CombatTarget stats;
        private PoisonEffect active;
        private float immunityTimer;

        /// <summary>Invoked when poison deals damage.</summary>
        public event System.Action<int> OnPoisonTick;

        /// <summary>Invoked when the poison effect ends.</summary>
        public event System.Action OnPoisonEnd;

        /// <summary>Returns true if this entity is currently immune to poison.</summary>
        public bool IsImmune => immunityTimer > 0f;

        /// <summary>Access to the current poison effect.</summary>
        public PoisonEffect ActiveEffect => active;

        /// <summary>Remaining time of poison immunity in seconds.</summary>
        public float ImmunityTimer
        {
            get => immunityTimer;
            set => immunityTimer = value;
        }

        private void Awake()
        {
            stats = statsComponent as CombatTarget ?? GetComponent<CombatTarget>();
        }

        /// <summary>
        /// Apply a poison configuration to this entity, refreshing if already poisoned.
        /// </summary>
        public void ApplyPoison(PoisonConfig cfg)
        {
            if (IsImmune || cfg == null)
                return;
            if (active == null)
            {
                active = new PoisonEffect(cfg);
                active.OnPoisonTick += dmg => OnPoisonTick?.Invoke(dmg);
                active.OnPoisonEnd += () => { OnPoisonEnd?.Invoke(); active = null; };
            }
            active.ApplyTo(stats);
        }

        /// <summary>
        /// Cure current poison and optionally grant temporary immunity.
        /// </summary>
        public void CurePoison(float immunitySeconds)
        {
            if (active != null)
            {
                active.ForceEnd();
                active = null;
            }
            immunityTimer = Mathf.Max(immunityTimer, immunitySeconds);
        }

        private void Update()
        {
            if (immunityTimer > 0f)
                immunityTimer = Mathf.Max(0f, immunityTimer - Time.deltaTime);
            if (active != null)
            {
                if (stats != null && stats.IsAlive)
                {
                    active.Tick(Time.deltaTime, DealTrueDamageBridge);
                }
                else
                {
                    active.ForceEnd();
                    active = null;
                }
            }
        }

        private void OnDisable()
        {
            if (active != null)
            {
                active.ForceEnd();
                active = null;
            }
        }

        /// <summary>
        /// Bridge method to apply true damage using the existing damage system.
        /// </summary>
        private bool DealTrueDamageBridge(int amount)
        {
            stats?.ApplyDamage(amount, DamageType.Poison, SpellElement.None, this);
            return true;
        }
    }
}
