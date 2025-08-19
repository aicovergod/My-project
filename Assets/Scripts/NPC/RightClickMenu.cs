using UnityEngine;
using UnityEngine.UI;
using Pets;
using Combat;

namespace NPC
{
    /// <summary>
    /// Simple right-click context menu used by NPCs.
    /// </summary>
    public class RightClickMenu : MonoBehaviour
    {
        public Button talkButton;
        public Button shopButton;
        public Button examineButton;
        public Button petAttackButton;

        private NpcInteractable current;

        private void Awake()
        {
            gameObject.SetActive(false);
            if (talkButton != null)
                talkButton.onClick.AddListener(() => { current?.Talk(); Hide(); });
            if (shopButton != null)
                shopButton.onClick.AddListener(() => { current?.OpenShop(); Hide(); });
            if (examineButton != null)
                examineButton.onClick.AddListener(() => { current?.Examine(); Hide(); });
            if (petAttackButton != null)
                petAttackButton.onClick.AddListener(() => { current?.AttackWithPet(); Hide(); });
        }

        public void Show(NpcInteractable npc, Vector2 position)
        {
            current = npc;
            transform.position = position;
            if (petAttackButton != null)
                petAttackButton.gameObject.SetActive(PetDropSystem.ActivePetCombat != null && npc.GetComponent<CombatTarget>() != null);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            current = null;
        }
    }
}
