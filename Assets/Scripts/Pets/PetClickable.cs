using UnityEngine;
using Inventory;

namespace Pets
{
    /// <summary>
    /// Detects clicks on the pet and converts it to an inventory item.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class PetClickable : MonoBehaviour
    {
        private PetDefinition definition;
        private PetStorage storage;

        public void Init(PetDefinition def, PetStorage petStorage)
        {
            definition = def;
            storage = petStorage;
        }

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void Update()
        {
            bool left = Input.GetMouseButtonDown(0);
            bool right = Input.GetMouseButtonDown(1);
            if (!left && !right)
                return;

            Vector3 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 p = new Vector2(world.x, world.y);
            var hit = Physics2D.OverlapPoint(p);
            if (hit != null && hit.gameObject == gameObject)
            {
                if (left)
                    OnLeftClick();
                else if (right)
                    OnRightClick();
            }
        }

        private void OnLeftClick()
        {
            if (definition != null && definition.pickupItem != null)
                InventoryBridge.AddItem(definition.pickupItem, 1);

            storage?.Close();
            PetDropSystem.DespawnActive();
            PetToastUI.Show("You pick up the pet.");
            Destroy(gameObject);
        }

        private void OnRightClick()
        {
            if (storage != null)
                storage.Open();
        }
    }
}
