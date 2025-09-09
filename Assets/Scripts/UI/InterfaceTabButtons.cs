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
            if (Object.FindObjectOfType<InterfaceTabButtons>() != null)
                return;

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

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-10f, 10f);
            rect.sizeDelta = new Vector2(400f, 100f);

            var layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 0f;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.LowerRight;

            AddButton(panel.transform, "QuestTab", ToggleQuest);
            AddButton(panel.transform, "InventoryTab", ToggleInventory);
            AddButton(panel.transform, "SkillTab", ToggleSkills);
            AddButton(panel.transform, "EquipmentTab", ToggleEquipment);
            AddButton(panel.transform, "AttackStyle", ToggleAttackStyle);
            AddButton(panel.transform, "MagicTab", ToggleMagic);
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
            rect.sizeDelta = new Vector2(100f, 100f);
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private void ToggleQuest()
        {
            var quest = Object.FindObjectOfType<QuestUI>();
            quest?.Toggle();
        }

        private void ToggleInventory()
        {
            var inventories = Object.FindObjectsOfType<Inventory.Inventory>();
            Inventory.Inventory playerInv = null;
            bool petOpen = false;

            foreach (var inv in inventories)
            {
                if (inv.GetComponent<Player.PlayerMover>() != null)
                    playerInv = inv;
                else if (inv.GetComponent<Pets.PetStorage>() != null && inv.IsOpen)
                    petOpen = true;
            }

            if (playerInv == null)
                return;

            if (playerInv.IsOpen || petOpen)
                playerInv.CloseUI();
            else
                playerInv.OpenUI();
        }

        private void ToggleSkills()
        {
            var skills = SkillsUI.Instance;
            skills?.Toggle();
        }

        private void ToggleEquipment()
        {
            var eq = Object.FindObjectOfType<Equipment>();
            if (eq != null)
            {
                if (eq.IsOpen)
                    eq.Close();
                else
                    eq.Open();
            }
        }

        private void ToggleAttackStyle()
        {
            var style = Object.FindObjectOfType<AttackStyleUI>();
            style?.Toggle();
        }

        private void ToggleMagic()
        {
            var magic = Object.FindObjectOfType<MagicUI>();
            magic?.Toggle();
        }

        // AttackStyleUI closes automatically through UIManager when other windows open.
    }
}

