using UnityEngine;
using UnityEngine.EventSystems;
using ShopSystem;
using Combat;
using Pets;

namespace NPC
{
    /// <summary>
    /// Opens the assigned shop when right-clicking this NPC without showing a context menu.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class NolofShopOpener : MonoBehaviour
    {
        [Tooltip("Shop component for this NPC.")]
        public Shop shop;

        private void OnMouseOver()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (Input.GetMouseButtonDown(1))
            {
                if (!PetDropSystem.GuardModeEnabled && PetDropSystem.ActivePetCombat != null && GetComponent<CombatTarget>() != null)
                {
                    PetDropSystem.ActivePetCombat.CommandAttack(GetComponent<CombatTarget>());
                    return;
                }

                if (shop == null) return;
                var ui = ShopUI.Instance;
                if (ui != null)
                    ui.Open(shop, GetComponent<NpcRandomMovement>());
            }
        }
    }
}
