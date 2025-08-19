using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Defines combat statistics for an NPC.
    /// </summary>
    [CreateAssetMenu(menuName = "Combat/NPC Profile")]
    public class NpcCombatProfile : ScriptableObject
    {
        public int AttackLevel = 1;
        public int StrengthLevel = 1;
        public int DefenceLevel = 1;
        public int HitpointsLevel = 10;
        public int MeleeDefence;
        public int RangeDefence;
        public int MagicDefence;
        public int AttackSpeedTicks = 4;
        public float RespawnSeconds;
        public DamageType AttackType = DamageType.Melee;
        public CombatStyle Style = CombatStyle.Accurate;
    }
}
