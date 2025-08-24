using System.Collections;
using UnityEngine;
using Combat;
using MyGame.Drops;

namespace NPC
{
    /// <summary>
    /// Simple adaptor tying an NPC to the combat system using a combat profile.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(NpcDropper))]
    public class NpcCombatant : MonoBehaviour, CombatTarget
    {
        [SerializeField] private NpcCombatProfile profile;
        private int currentHp;
        private Collider2D collider2D;
        private SpriteRenderer spriteRenderer;

        public event System.Action<int, int> OnHealthChanged; // current, max
        public event System.Action OnDeath;

        public bool IsAlive => currentHp > 0;
        public DamageType PreferredDefenceType => profile != null ? profile.AttackType : DamageType.Melee;
        public int CurrentHP => currentHp;
        public int MaxHP => profile != null ? profile.HitpointsLevel : currentHp;
        public NpcCombatProfile Profile => profile;

        private void Awake()
        {
            currentHp = profile != null ? profile.HitpointsLevel : 1;
            collider2D = GetComponent<Collider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            OnHealthChanged?.Invoke(currentHp, MaxHP);
        }

        /// <summary>Apply damage to this NPC.</summary>
        public void ApplyDamage(int amount, DamageType type, object source)
        {
            currentHp = Mathf.Max(0, currentHp - amount);
            Debug.Log($"{name} took {amount} damage ({currentHp}/{MaxHP}).");
            OnHealthChanged?.Invoke(currentHp, MaxHP);
            if (currentHp <= 0)
            {
                // Trigger drops before other death listeners in case they
                // destroy this NPC immediately (e.g. when killed by pets).
                var dropper = GetComponent<NpcDropper>();
                dropper?.OnDeath();

                OnDeath?.Invoke();
                if (collider2D) collider2D.enabled = false;
                if (spriteRenderer) spriteRenderer.enabled = false;
                if (profile != null && profile.RespawnSeconds > 0f)
                    StartCoroutine(RespawnRoutine());
            }
        }

        /// <summary>Get combat stats for this NPC.</summary>
        public CombatantStats GetCombatantStats()
        {
            return CombatantStats.ForNpc(profile);
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(profile.RespawnSeconds);
            currentHp = profile != null ? profile.HitpointsLevel : 1;
            if (collider2D) collider2D.enabled = true;
            if (spriteRenderer) spriteRenderer.enabled = true;
            OnHealthChanged?.Invoke(currentHp, MaxHP);
        }
    }
}
