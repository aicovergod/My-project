using UnityEngine;
using Skills;
using Combat;

namespace Pets
{
    /// <summary>
    /// Adapter that bridges the existing pet system with <see cref="IPetService"/>.
    /// </summary>
    public class PetServiceAdapter : MonoBehaviour, IPetService
    {
        public bool TryGetActiveCombatPet(out PetHandle pet)
        {
            var go = PetDropSystem.ActivePetObject;
            if (go != null)
            {
                pet = go.GetComponent<PetHandle>();
                if (pet == null)
                    pet = go.AddComponent<PetHandle>();
                return true;
            }
            pet = null;
            return false;
        }

        public void HideActivePet()
        {
            var go = PetDropSystem.ActivePetObject;
            if (go != null)
                go.SetActive(false);
        }

        public void ShowActivePet(Vector3 worldPosition)
        {
            var go = PetDropSystem.ActivePetObject;
            if (go != null)
            {
                go.transform.position = worldPosition;
                go.SetActive(true);
            }
        }

        public PetVisualProfile GetVisuals(PetHandle pet)
        {
            var profile = new PetVisualProfile();
            if (pet != null)
            {
                profile.localScale = pet.transform.localScale;
                var anim = pet.GetComponent<Animator>();
                if (anim != null)
                    profile.controller = anim.runtimeAnimatorController;
                var sr = pet.GetComponent<SpriteRenderer>();
                if (sr != null)
                    profile.baseSprite = sr.sprite;
                var psa = pet.GetComponent<PetSpriteAnimator>();
                if (psa != null)
                {
                    if (psa.idleDown != null && psa.idleDown.Length > 0) profile.idleDown = psa.idleDown[0];
                    if (psa.idleLeft != null && psa.idleLeft.Length > 0) profile.idleLeft = psa.idleLeft[0];
                    if (psa.idleRight != null && psa.idleRight.Length > 0) profile.idleRight = psa.idleRight[0];
                    if (psa.idleUp != null && psa.idleUp.Length > 0) profile.idleUp = psa.idleUp[0];
                    if (psa.walkDown != null && psa.walkDown.Length > 0) profile.walkDown = psa.walkDown[0];
                    if (psa.walkLeft != null && psa.walkLeft.Length > 0) profile.walkLeft = psa.walkLeft[0];
                    if (psa.walkRight != null && psa.walkRight.Length > 0) profile.walkRight = psa.walkRight[0];
                    if (psa.walkUp != null && psa.walkUp.Length > 0) profile.walkUp = psa.walkUp[0];
                    if (psa.hitDown != null && psa.hitDown.Length > 0) profile.hitDown = psa.hitDown;
                    if (psa.hitLeft != null && psa.hitLeft.Length > 0) profile.hitLeft = psa.hitLeft;
                    if (psa.hitRight != null && psa.hitRight.Length > 0) profile.hitRight = psa.hitRight;
                    if (psa.hitUp != null && psa.hitUp.Length > 0) profile.hitUp = psa.hitUp;
                    profile.useFlipXForLeft = psa.useFlipXForLeft;
                    profile.useFlipXForRight = psa.useFlipXForRight;
                }
            }
            return profile;
        }

        public ICombatProfile GetCombatProfile(PetHandle pet)
        {
            var combat = pet != null ? pet.GetComponent<PetCombatController>() : null;
            if (combat == null)
                return null;
            return new PetCombatProfileAdapter(combat);
        }

        private class PetCombatProfileAdapter : ICombatProfile
        {
            private readonly PetCombatController controller;

            public PetCombatProfileAdapter(PetCombatController controller)
            {
                this.controller = controller;
            }

            public CombatantStats GetCombatStats()
            {
                if (controller == null || controller.definition == null)
                    return new CombatantStats();

                var def = controller.definition;
                var stats = new CombatantStats
                {
                    AttackLevel = def.petAttackLevel,
                    StrengthLevel = def.petStrengthLevel,
                    DefenceLevel = 1,
                    Equip = new EquipmentSystem.EquipmentAggregator.CombinedStats
                    {
                        attack = def.accuracyBonus,
                        strength = def.damageBonus,
                        attackSpeedTicks = def.attackSpeedTicks
                    },
                    Style = CombatStyle.Accurate,
                    DamageType = DamageType.Melee
                };

                var exp = controller.GetComponent<PetExperience>();
                float mult = exp != null ? PetExperience.GetStatMultiplier(exp.Level) : 1f;
                stats.AttackLevel = Mathf.RoundToInt(stats.AttackLevel * mult);
                stats.StrengthLevel = Mathf.RoundToInt(stats.StrengthLevel * mult);
                stats.Equip.attack = Mathf.RoundToInt(stats.Equip.attack * mult);
                stats.Equip.strength = Mathf.RoundToInt(stats.Equip.strength * mult);

                var follower = controller.GetComponent<PetFollower>();
                var owner = follower != null ? follower.Player : null;
                int bmLevel = 1;
                if (owner != null && owner.TryGetComponent<SkillManager>(out var skills))
                    bmLevel = skills.GetLevel(SkillType.Beastmaster);
                if (def.attackLevelPerBeastmasterLevel != 0f)
                    stats.AttackLevel = Mathf.RoundToInt(stats.AttackLevel * (1f + def.attackLevelPerBeastmasterLevel * bmLevel));
                if (def.strengthLevelPerBeastmasterLevel != 0f)
                    stats.StrengthLevel = Mathf.RoundToInt(stats.StrengthLevel * (1f + def.strengthLevelPerBeastmasterLevel * bmLevel));

                return stats;
            }
        }
    }
}
