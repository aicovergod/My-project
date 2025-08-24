using UnityEngine;
using UnityEngine.UI;
using Inventory;
using Quests;
using Skills;
using Object = UnityEngine.Object;

namespace UI
{
    /// <summary>
    /// Creates tab buttons in the bottom-right corner for quick access to
    /// quest, inventory, skill and equipment interfaces.
    /// </summary>
    public class InterfaceTabButtons : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var go = new GameObject("InterfaceTabButtons");
            DontDestroyOnLoad(go);
            go.AddComponent<InterfaceTabButtons>();
        }

        private void Awake()
        {
            CreateUI();
        }

        private void CreateUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-10f, 0f);

            var layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 5f;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            AddButton(panel.transform, "QuestTab", ToggleQuest);
            AddButton(panel.transform, "InventoryTab", ToggleInventory);
            AddButton(panel.transform, "SkillTab", ToggleSkills);
            AddButton(panel.transform, "EquipmentTab", ToggleEquipment);
        }

        private void AddButton(Transform parent, string spriteName, UnityEngine.Events.UnityAction onClick)
        {
            var sprite = Resources.Load<Sprite>("Interfaces/UIButtons/" + spriteName);
            var go = new GameObject(spriteName, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(120f, 120f);
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private void ToggleQuest()
        {
            var quest = Object.FindObjectOfType<QuestUI>();
            quest?.Toggle();
        }

        private void ToggleInventory()
        {
            var inv = Object.FindObjectOfType<Inventory.Inventory>();
            if (inv != null)
            {
                if (inv.IsOpen)
                    inv.CloseUI();
                else
                    inv.OpenUI();
            }
        }

        private void ToggleSkills()
        {
            var skills = SkillsUI.Instance;
            skills?.Toggle();
        }

        private void ToggleEquipment()
        {
            var eq = Object.FindObjectOfType<Equipment>();
            eq?.ToggleUI();
        }
    }
}

