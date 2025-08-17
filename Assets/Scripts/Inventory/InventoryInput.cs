using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// Handles input for toggling the inventory UI.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventoryModel), typeof(InventoryUI), typeof(InventoryDragHandler), typeof(InventoryTooltip))]
    public class InventoryInput : MonoBehaviour
    {
        private InventoryUI ui;

        private void Awake()
        {
            ui = GetComponent<InventoryUI>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.I))
            {
                if (ui.IsOpen)
                    ui.CloseUI();
                else
                    ui.OpenUI();
            }
        }
    }
}
