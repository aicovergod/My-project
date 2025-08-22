using UnityEngine;
using Inventory;

namespace Pets
{
    /// <summary>
    /// Provides per-pet storage using the project's inventory system.
    /// </summary>
    [DisallowMultipleComponent]
    public class PetStorage : MonoBehaviour
    {
        public PetDefinition definition;
        private Inventory.Inventory inventory;
        private PetExperience experience;

        private void Awake()
        {
            experience = GetComponent<PetExperience>();
        }

        private void Start()
        {
            if (definition != null && definition.hasInventory)
            {
                CreateInventory();
                if (experience != null)
                    experience.OnLevelChanged += HandleLevelChanged;
            }
        }

        private void OnDestroy()
        {
            if (experience != null)
                experience.OnLevelChanged -= HandleLevelChanged;
        }

        private void CreateInventory()
        {
            inventory = gameObject.AddComponent<Inventory.Inventory>();
            inventory.useSharedUIRoot = false;
            inventory.columns = 4;
            inventory.showCloseButton = false;
            inventory.emptySlotColor = Color.clear;
            inventory.centerOnScreen = true;
            inventory.size = GetSlotsForLevel(experience != null ? experience.Level : 1);
            inventory.saveKey = $"PetInv_{definition?.id}";
            var inventories = FindObjectsOfType<Inventory.Inventory>();
            foreach (var inv in inventories)
            {
                if (inv.gameObject != gameObject)
                {
                    inventory.windowColor = inv.windowColor;
                    break;
                }
            }
        }

        private void HandleLevelChanged(int lvl)
        {
            if (inventory == null)
                return;
            int newSize = GetSlotsForLevel(lvl);
            if (newSize != inventory.size)
            {
                inventory.Save();
                Destroy(inventory);
                CreateInventory();
            }
        }

        private int GetSlotsForLevel(int level)
        {
            if (level >= 99) return 20;
            if (level >= 75) return 16;
            if (level >= 50) return 12;
            if (level >= 25) return 8;
            return 4;
        }

        public void Open()
        {
            if (inventory != null)
                inventory.OpenUI();
        }

        public void Close()
        {
            if (inventory != null)
                inventory.CloseUI();
        }
    }
}
