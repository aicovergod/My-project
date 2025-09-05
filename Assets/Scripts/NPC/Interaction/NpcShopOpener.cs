using UnityEngine;
using UnityEngine.EventSystems;
using ShopSystem;
using Combat;
using Pets;

namespace NPC
{
    /// <summary>
    /// Opens an NPC's shop either directly on right-click or via context menu through <see cref="NpcInteractable"/>.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class NpcShopOpener : MonoBehaviour
    {
        [Tooltip("Shop component for this NPC. If not assigned, will look on this GameObject.")]
        public Shop shop;

        [Tooltip("If true, open the shop immediately when right-clicked.")]
        public bool openDirectly;

        private NpcInteractable interactable;

        private void Awake()
        {
            if (shop == null)
                shop = GetComponent<Shop>();
            interactable = GetComponent<NpcInteractable>();
            if (!openDirectly && interactable != null)
                interactable.shop = shop;
        }

        private void OnMouseOver()
        {
            if (!openDirectly)
                return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            if (Input.GetMouseButtonDown(1))
            {
                if (!PetDropSystem.GuardModeEnabled && PetDropSystem.ActivePetCombat != null && GetComponent<CombatTarget>() != null)
                {
                    PetDropSystem.ActivePetCombat.CommandAttack(GetComponent<CombatTarget>());
                    return;
                }

                OpenShop();
            }
        }

        public void OpenShop()
        {
            if (shop == null) return;
            var ui = ShopUI.Instance;
            if (ui != null)
                ui.Open(shop, GetComponent<NpcWanderer>());
        }
    }
}
