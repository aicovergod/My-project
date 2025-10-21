using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using NPC;

namespace UI
{
    /// <summary>
    /// Simple right-click context menu used by NPCs.
    /// </summary>
    public class RightClickMenu : MonoBehaviour
    {
        public Button talkButton;
        public Button shopButton;
        public Button examineButton;

        private NpcInteractable current;

        /// <summary>Cached delegate used when wiring the Talk button listener.</summary>
        private UnityAction talkButtonHandler;

        /// <summary>Cached delegate used when wiring the Trade button listener.</summary>
        private UnityAction shopButtonHandler;

        /// <summary>Cached delegate used when wiring the Examine button listener.</summary>
        private UnityAction examineButtonHandler;

        private void Awake()
        {
            talkButtonHandler = HandleTalkPressed;
            shopButtonHandler = HandleShopPressed;
            examineButtonHandler = HandleExaminePressed;

            ConfigureButtons(talkButton, shopButton, examineButton);
            Hide();
        }

        /// <summary>
        ///     Wires the provided buttons to the context menu actions. Allows runtime builders to
        ///     replace the references after <see cref="Awake"/> executes when the prefab is missing.
        /// </summary>
        public void ConfigureButtons(Button talk, Button shop, Button examine)
        {
            AssignButton(ref talkButton, talk, talkButtonHandler);
            AssignButton(ref shopButton, shop, shopButtonHandler);
            AssignButton(ref examineButton, examine, examineButtonHandler);
        }

        public void Show(NpcInteractable npc, Vector2 position)
        {
            current = npc;
            transform.position = position;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            current = null;
        }

        /// <summary>
        ///     Removes the previous listener and attaches the provided handler to the supplied button.
        /// </summary>
        private static void AssignButton(ref Button field, Button newButton, UnityAction handler)
        {
            if (field == newButton)
                return;

            if (field != null && handler != null)
                field.onClick.RemoveListener(handler);

            field = newButton;

            if (field != null && handler != null)
                field.onClick.AddListener(handler);
        }

        /// <summary>Handles the Talk option by forwarding to the interactable and hiding the menu.</summary>
        private void HandleTalkPressed()
        {
            current?.Talk();
            Hide();
        }

        /// <summary>Handles the Trade option by forwarding to the interactable and hiding the menu.</summary>
        private void HandleShopPressed()
        {
            current?.OpenShop();
            Hide();
        }

        /// <summary>Handles the Examine option by forwarding to the interactable and hiding the menu.</summary>
        private void HandleExaminePressed()
        {
            current?.Examine();
            Hide();
        }
    }
}
