using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Inventory
{
    /// <summary>
    /// Simple debug menu that lets the developer spawn any <see cref="ItemData"/>
    /// into the player's <see cref="Inventory"/>. Press <c>F1</c> to toggle the
    /// menu. When the menu is open, a button is shown for each item. Clicking a
    /// button adds that item to the inventory.
    ///
    /// In the editor all items found under <c>Assets/Item</c> and
    /// <c>Assets/Resources/Item</c> are listed using <c>AssetDatabase</c>. In a
    /// player build it falls back to loading items from a <c>Resources/Item</c>
    /// folder.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryDebugMenu : MonoBehaviour
    {
        public static InventoryDebugMenu Instance;

        [Tooltip("Inventory to add items to. If not set the component tries to find one in the scene.")]
        public Inventory inventory;

        private ItemData[] allItems = new ItemData[0];
        private Vector2 scroll;
        private bool visible;
        private ItemData amountItem;
        private string amountText = "1";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (inventory == null)
            {
                inventory = FindObjectOfType<Inventory>();
            }

#if UNITY_EDITOR
            // In the editor load all ItemData assets from Assets/Item and
            // Assets/Resources/Item
            string[] guids = AssetDatabase.FindAssets(
                "t:ItemData",
                new[] { "Assets/Item", "Assets/Resources/Item" });
            allItems = new ItemData[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                allItems[i] = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            }
#else
            // At runtime try to load all items from a Resources/Item folder
            allItems = Resources.LoadAll<ItemData>("Item");
#endif
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            if (!visible || allItems == null)
                return;

            const float width = 200f;
            const float height = 300f;
            Rect area = new Rect(10f, 10f, width, height);
            GUILayout.BeginArea(area, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            foreach (var item in allItems)
            {
                if (item != null)
                {
                    Rect rect = GUILayoutUtility.GetRect(new GUIContent(item.name), GUI.skin.button);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && rect.Contains(Event.current.mousePosition))
                    {
                        amountItem = item;
                        amountText = "1";
                        Event.current.Use();
                    }
                    if (GUI.Button(rect, item.name) && Event.current.button == 0)
                    {
                        inventory?.AddItem(item);
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (amountItem != null)
            {
                Rect popup = new Rect(area.x + width + 10f, area.y, 180f, 100f);
                GUILayout.BeginArea(popup, GUI.skin.box);
                GUILayout.Label($"Spawn {amountItem.name}");
                GUI.SetNextControlName("AmountField");
                amountText = GUILayout.TextField(amountText);
                GUI.FocusControl("AmountField");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("OK"))
                {
                    if (int.TryParse(amountText, out int n))
                    {
                        for (int i = 0; i < n; i++)
                        {
                            inventory?.AddItem(amountItem);
                        }
                    }
                    amountItem = null;
                }
                if (GUILayout.Button("Cancel"))
                {
                    amountItem = null;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
        }
    }
}
