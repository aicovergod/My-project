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

        public void Init(PetDefinition def)
        {
            definition = def;
        }

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Vector3 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                Vector2 p = new Vector2(world.x, world.y);
                var hit = Physics2D.OverlapPoint(p);
                if (hit != null && hit.gameObject == gameObject)
                    OnClicked();
            }
        }

        private void OnClicked()
        {
            if (definition != null && definition.pickupItem != null)
                InventoryBridge.AddItem(definition.pickupItem, 1);

            PetDropSystem.DespawnActive();
            PetToastUI.Show("You pick up the pet.");
            Destroy(gameObject);
        }
    }
}
