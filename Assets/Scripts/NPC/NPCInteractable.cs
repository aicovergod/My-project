using UnityEngine;
using UnityEngine.UI;
using ShopSystem;

namespace NPC
{
    /// <summary>
    /// Allows the player to interact with an NPC via right-click context menu.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class NpcInteractable : MonoBehaviour
    {
        [Tooltip("Optional shop component for this NPC.")]
        public Shop shop;

        [Tooltip("Context menu prefab that provides Talk / Open Shop / Examine.")]
        public RightClickMenu menuPrefab;

        // Shared instance so the menu persists across scene loads
        private static RightClickMenu menuInstance;
        private static Canvas menuCanvas;

        private void OnMouseOver()
        {
            if (Input.GetMouseButtonDown(1))
            {
                if (menuInstance == null)
                {
                    var canvasGO = new GameObject("ContextMenuCanvas", typeof(Canvas), typeof(CanvasScaler),
                        typeof(GraphicRaycaster));
                    menuCanvas = canvasGO.GetComponent<Canvas>();
                    menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    DontDestroyOnLoad(canvasGO);

                    menuInstance = Instantiate(menuPrefab, menuCanvas.transform);
                }

                menuInstance.Show(this, Input.mousePosition);
            }
        }

        public void Talk()
        {
            Debug.Log($"{name} has nothing to say yet.");
        }

        public void OpenShop()
        {
            if (shop == null) return;
            var ui = ShopUI.Instance;
            if (ui != null)
            {
                ui.Open(shop, GetComponent<NpcRandomMovement>());
            }
        }

        public void Examine()
        {
            Debug.Log($"You examine {name}.");
        }
    }
}
