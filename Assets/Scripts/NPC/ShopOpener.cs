using UnityEngine;
using UnityEngine.EventSystems;
using ShopSystem;
using Combat;
using Pets;

namespace NPC
{
    /// <summary>
    /// Opens an NPC's shop when right-clicked, if a <see cref="Shop"/> component is present.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class ShopOpener : MonoBehaviour
    {
        [Tooltip("Optional shop component. If not assigned, will look on this GameObject.")]
        public Shop shop;

        private void Awake()
        {
            if (shop == null)
                shop = GetComponent<Shop>();
        }

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
