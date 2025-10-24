using System;
using UnityEngine;
using Player.Ranks;
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

        /// <summary>
        /// Name assigned to the search text field so other systems can detect when it owns the
        /// keyboard focus.
        /// </summary>
        private const string SearchControlName = "InventoryDebugMenu_Search";

        /// <summary>
        /// Name assigned to the amount text field displayed when right-clicking an item button.
        /// </summary>
        private const string AmountControlName = "InventoryDebugMenu_Amount";

        /// <summary>
        /// Indicates whether any text field inside the debug menu currently has keyboard focus.
        /// Movement systems query this so typing in the menu does not trigger gameplay input.
        /// </summary>
        public static bool HasTextInputFocus { get; private set; }

        /// <summary>True while the debug menu window is visible.</summary>
        public bool Visible => visible;

        [Tooltip("Inventory to add items to. If not set the component tries to find one in the scene.")]
        public Inventory inventory;

        private ItemData[] allItems = new ItemData[0];
        private Vector2 scroll;
        private bool visible;
        private ItemData amountItem;
        private string amountText = "1";
        private string searchText = string.Empty;

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
            bool hasDeveloperAccess = HasDeveloperAccess();

            if (!hasDeveloperAccess)
            {
                if (visible)
                {
                    // Immediately close the menu when the active account loses developer access so
                    // privileged tooling is never exposed to lower ranks.
                    visible = false;
                    amountItem = null;
                    HasTextInputFocus = false;
                }

                return;
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                visible = !visible;

                if (!visible)
                {
                    // Close the amount popup and release keyboard focus whenever the menu hides so
                    // gameplay input can resume immediately.
                    amountItem = null;
                    HasTextInputFocus = false;
                }
            }
        }

        private void OnGUI()
        {
            if (!visible || allItems == null)
            {
                HasTextInputFocus = false;
                return;
            }

            // Reset the focus flag for this repaint; it will be re-enabled below if one of the text
            // fields has focus. Doing the reset here ensures the property reflects the current GUI
            // state even when Unity renders the window multiple times per frame.
            HasTextInputFocus = false;

            const float width = 200f;
            const float height = 300f;
            Rect area = new Rect(10f, 10f, width, height);
            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.Label("Search:");
            GUI.SetNextControlName(SearchControlName);
            searchText = GUILayout.TextField(searchText);
            if (GUI.GetNameOfFocusedControl() == SearchControlName)
                HasTextInputFocus = true;

            scroll = GUILayout.BeginScrollView(scroll);

            foreach (var item in allItems)
            {
                if (item != null && (string.IsNullOrEmpty(searchText) || item.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
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
                GUI.SetNextControlName(AmountControlName);
                amountText = GUILayout.TextField(amountText);
                if (GUI.GetNameOfFocusedControl() == AmountControlName)
                    HasTextInputFocus = true;
                GUI.FocusControl(AmountControlName);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("OK"))
                {
                    if (int.TryParse(amountText, out int n))
                    {
                        inventory?.AddItem(amountItem, n);
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

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            HasTextInputFocus = false;
        }

        /// <summary>
        /// Determines whether the currently authenticated account has access to developer-only tooling.
        /// </summary>
        private static bool HasDeveloperAccess()
        {
            var rankService = PlayerRankService.Instance;
            if (rankService == null)
                return false;

            return rankService.HasPermission(rankService.ActivePlayerRank, PlayerRank.Developer);
        }
    }
}
