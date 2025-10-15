using System.Collections;
using System.Collections.Generic;
using Player;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using InventoryComponent = global::Inventory.Inventory;

namespace Inventory.GroundItems
{
    /// <summary>
    /// Scene-level registry that reproduces Old School RuneScape-style ground item selection.
    /// Attach this to a scene manager object (for example the GameManager root) so it can
    /// track active <see cref="ItemPickup"/> instances by tile, surface a dropdown menu when
    /// multiple items share a tile, and direct the player mover to collect the chosen item.
    /// <para>
    /// Manual validation: spawn multiple drops on one tile, left-click single-item tiles (no menu),
    /// select an entry from multi-item tiles, attempt pickup with a full inventory, cancel auto-walk
    /// mid-run, and ensure the menu closes if items despawn externally.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class GroundItemManager : MonoBehaviour
    {
        private static GroundItemManager instance;

        [Header("References")]
        [SerializeField]
        [Tooltip("Primary player inventory. Auto-assigned if left null.")]
        private InventoryComponent playerInventory;

        [SerializeField]
        [Tooltip("Player auto-movement service responsible for pathing to ground items.")]
        private PlayerMover playerMover;

        [SerializeField]
        [Tooltip("Camera used for world interaction. Automatically gains a Physics2DRaycaster if missing.")]
        private Camera worldCamera;

        [Header("Pickup Behaviour")]
        [SerializeField]
        [Tooltip("Stop distance supplied to PlayerMover.MoveTo when auto-walking to a ground item tile.")]
        private float pickupStopDistance = 0.1f;

        [SerializeField]
        [Tooltip("Pixels of cursor padding that keep the menu open before it auto-closes.")]
        private float menuSafePadding = 12f;

        [SerializeField]
        [Tooltip("Layer mask applied to the Physics2DRaycaster on the world camera.")]
        private LayerMask itemRaycastMask = Physics2D.DefaultRaycastLayers;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("When enabled the manager prints verbose registration and pickup flow messages.")]
        private bool enableDebugLogging;

        private readonly Dictionary<Vector2Int, List<ItemPickup>> pickupsByTile = new Dictionary<Vector2Int, List<ItemPickup>>();
        private readonly Dictionary<ItemPickup, Vector2Int> pickupTileLookup = new Dictionary<ItemPickup, Vector2Int>();

        private long nextSpawnOrder = 1;

        private Coroutine activePickupCoroutine;
        private ItemPickup activePickup;
        private Vector2Int activePickupTile;
        private Vector3 activePickupLastKnownPosition;

        private GroundItemPickupMenu pickupMenu;
        private Vector2Int? openMenuTile;

        /// <summary>Singleton accessor for the scene-level manager.</summary>
        public static GroundItemManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<GroundItemManager>();
                }

                return instance;
            }
        }

        /// <summary>Utility that converts a world position into its corresponding tile coordinate.</summary>
        public static Vector2Int GetTileForPosition(Vector2 position)
        {
            return new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                if (enableDebugLogging)
                    Debug.LogWarning("GroundItemManager duplicate detected and destroyed.");
                Destroy(gameObject);
                return;
            }

            instance = this;

            playerInventory ??= FindObjectOfType<InventoryComponent>(true);
            playerMover ??= FindObjectOfType<PlayerMover>(true);
            worldCamera ??= Camera.main;

            EnsurePhysicsRaycaster();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>Registers a new ground item pickup into the tile registry.</summary>
        public void RegisterPickup(ItemPickup pickup)
        {
            if (pickup == null)
                return;

            Vector2Int tile = GetTileForPosition(pickup.transform.position);
            pickup.CacheTile(tile);
            pickup.SpawnOrder = nextSpawnOrder++;

            if (!pickupsByTile.TryGetValue(tile, out var list))
            {
                list = new List<ItemPickup>();
                pickupsByTile.Add(tile, list);
            }

            if (!list.Contains(pickup))
            {
                list.Add(pickup);
                SortBySpawnOrder(list);
            }

            pickupTileLookup[pickup] = tile;

            if (enableDebugLogging)
                Debug.Log($"[GroundItemManager] Registered pickup {pickup.name} on tile {tile} (spawn {pickup.SpawnOrder}).");

            RefreshMenuIfTrackingTile(tile);
        }

        /// <summary>Removes a pickup from the registry when it despawns or is collected.</summary>
        public void UnregisterPickup(ItemPickup pickup)
        {
            if (pickup == null)
                return;

            if (!pickupTileLookup.TryGetValue(pickup, out var tile))
                return;

            pickupTileLookup.Remove(pickup);

            if (pickupsByTile.TryGetValue(tile, out var list))
            {
                list.Remove(pickup);
                if (list.Count == 0)
                {
                    pickupsByTile.Remove(tile);
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[GroundItemManager] Unregistered pickup {pickup.name} from tile {tile}.");

            RefreshMenuIfTrackingTile(tile);

            if (activePickup == pickup)
            {
                AbortActivePickup("Pickup was removed before collection could complete.");
            }
        }

        /// <summary>Processes a left-click on a ground item and opens the selection menu if necessary.</summary>
        public void HandlePickupClick(ItemPickup pickup, Vector3 clickScreenPosition)
        {
            if (pickup == null)
                return;

            Vector2Int tile = pickup.TileCoordinate;
            var pickups = GetPickupsOnTile(tile);

            if (pickups.Count == 0)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[GroundItemManager] No pickups registered on tile {tile} for clicked item {pickup.name}.");
                HideMenu();
                return;
            }

            if (pickups.Count == 1)
            {
                HideMenu();
                BeginPickupRoutine(pickups[0]);
                return;
            }

            ShowMenu(tile, pickups, clickScreenPosition);
        }

        private void EnsurePhysicsRaycaster()
        {
            if (worldCamera == null)
                return;

            var raycaster = worldCamera.GetComponent<Physics2DRaycaster>();
            if (raycaster == null)
            {
                raycaster = worldCamera.gameObject.AddComponent<Physics2DRaycaster>();
                if (enableDebugLogging)
                    Debug.Log("[GroundItemManager] Added Physics2DRaycaster to world camera for ground item clicks.");
            }

            raycaster.eventMask = itemRaycastMask;
        }

        private bool EnsurePlayerMover()
        {
            // When the cached mover reference exists but is currently inactive we should
            // treat it as unresolved so we can search for an active instance instead of
            // trying to issue movement commands against a disabled component.
            if (playerMover != null)
            {
                bool moverActive = playerMover.isActiveAndEnabled && playerMover.gameObject.activeInHierarchy;
                if (moverActive)
                    return true;

                if (enableDebugLogging)
                    Debug.Log("[GroundItemManager] Cached PlayerMover was inactive; attempting to reacquire an active instance.");

                playerMover = null;
            }

            // Search the scene for an active mover. We include inactive objects in the
            // search so we can verify their state before use and pick the first active
            // candidate that is ready to accept movement requests.
            var movers = FindObjectsOfType<PlayerMover>(true);
            foreach (var mover in movers)
            {
                if (mover != null && mover.isActiveAndEnabled && mover.gameObject.activeInHierarchy)
                {
                    playerMover = mover;
                    if (enableDebugLogging)
                        Debug.Log("[GroundItemManager] Resolved active PlayerMover reference at runtime.");
                    return true;
                }
            }

            Debug.LogWarning("[GroundItemManager] PlayerMover reference missing or inactive; cannot auto-pickup.");
            return false;
        }

        private List<ItemPickup> GetPickupsOnTile(Vector2Int tile)
        {
            if (!pickupsByTile.TryGetValue(tile, out var list))
                return new List<ItemPickup>();

            // Remove any stale references before building the outgoing list.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null)
                    list.RemoveAt(i);
            }

            if (list.Count == 0)
            {
                pickupsByTile.Remove(tile);
                return new List<ItemPickup>();
            }

            SortBySpawnOrder(list);

            return new List<ItemPickup>(list);
        }

        private void SortBySpawnOrder(List<ItemPickup> pickups)
        {
            pickups.Sort((a, b) => a.SpawnOrder.CompareTo(b.SpawnOrder));
        }

        private void ShowMenu(Vector2Int tile, List<ItemPickup> pickups, Vector2 screenPosition)
        {
            EnsureMenuInstance();
            pickupMenu.SafePadding = menuSafePadding;
            openMenuTile = tile;
            pickupMenu.Show(pickups, screenPosition, OnMenuSelection);

            if (enableDebugLogging)
                Debug.Log($"[GroundItemManager] Showing ground item menu for tile {tile} with {pickups.Count} entries.");
        }

        private void HideMenu()
        {
            if (pickupMenu == null)
                return;

            pickupMenu.Hide();
            openMenuTile = null;
        }

        private void EnsureMenuInstance()
        {
            if (pickupMenu != null)
                return;

            var go = new GameObject("GroundItemPickupMenu");
            pickupMenu = go.AddComponent<GroundItemPickupMenu>();
            pickupMenu.SafePadding = menuSafePadding;
            pickupMenu.MenuHidden += OnMenuHidden;
        }

        private void OnMenuSelection(ItemPickup pickup)
        {
            HideMenu();
            BeginPickupRoutine(pickup);
        }

        private void OnMenuHidden()
        {
            openMenuTile = null;
        }

        private void RefreshMenuIfTrackingTile(Vector2Int tile)
        {
            if (!openMenuTile.HasValue || pickupMenu == null)
                return;

            if (openMenuTile.Value != tile)
                return;

            var pickups = GetPickupsOnTile(tile);
            pickupMenu.RefreshFrom(pickups);

            if (pickups.Count == 0)
            {
                HideMenu();
            }
        }

        private void BeginPickupRoutine(ItemPickup pickup)
        {
            if (pickup == null)
                return;

            if (!EnsurePlayerMover())
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[GroundItemManager] Cannot auto-pickup without an active PlayerMover reference.");
                return;
            }

            CancelActivePickupRoutine();

            activePickup = pickup;
            activePickupTile = pickup.TileCoordinate;
            activePickupLastKnownPosition = pickup.transform.position;

            activePickupCoroutine = StartCoroutine(ExecutePickupRoutine(pickup));
        }

        private IEnumerator ExecutePickupRoutine(ItemPickup pickup)
        {
            bool arrivalCallbackTriggered = false;
            Vector3 lastIssuedDestination = pickup != null ? pickup.transform.position : Vector3.zero;

            void OnArrived()
            {
                arrivalCallbackTriggered = true;
            }

            if (!EnsurePlayerMover())
            {
                AbortActivePickup("Cannot auto-pickup because the PlayerMover could not be located.");
                yield break;
            }

            IssueMoveCommand(lastIssuedDestination, OnArrived);

            while (pickup != null)
            {
                if (pickup == null)
                    break;

                if (!pickup.gameObject.activeInHierarchy)
                {
                    HandlePickupUnavailable();
                    yield break;
                }

                activePickupLastKnownPosition = pickup.transform.position;

                float distance = Vector2.Distance(playerMover.transform.position, pickup.transform.position);
                if (distance <= pickup.PickupRadius + pickupStopDistance)
                {
                    AttemptCollection(pickup);
                    yield break;
                }

                // Respect manual movement cancellation before we consider retargeting. If the
                // player has issued their own movement command we should immediately abandon
                // the auto pickup so the new command is honoured.
                if (!playerMover.IsAutoMoving && !arrivalCallbackTriggered)
                {
                    if (enableDebugLogging)
                        Debug.Log("[GroundItemManager] Auto pickup cancelled because the player interrupted movement.");
                    CancelActivePickupRoutine();
                    yield break;
                }

                // If the pickup is pushed or otherwise displaced while we are moving towards it,
                // retarget the PlayerMover so we walk directly to the live world position instead
                // of the tile center that was originally clicked.
                if (!arrivalCallbackTriggered)
                {
                    Vector3 currentDestination = pickup.transform.position;
                    if ((currentDestination - lastIssuedDestination).sqrMagnitude > 0.0001f)
                    {
                        lastIssuedDestination = currentDestination;
                        IssueMoveCommand(currentDestination, OnArrived);
                    }
                }

                yield return null;
            }

            CancelActivePickupRoutine();

            void IssueMoveCommand(Vector3 destination, System.Action arrivedCallback)
            {
                playerMover.MoveTo((Vector2)destination, pickupStopDistance, arrivedCallback);
            }
        }

        private void AttemptCollection(ItemPickup pickup)
        {
            if (playerInventory == null)
                playerInventory = FindObjectOfType<InventoryComponent>(true);

            if (playerInventory == null)
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[GroundItemManager] Pickup failed because no Inventory component was located.");
                CancelActivePickupRoutine();
                return;
            }

            bool success = pickup.TryCollect(playerInventory);
            if (!success)
            {
                FloatingText.Show("Inventory full", pickup.transform.position);
                if (enableDebugLogging)
                    Debug.Log("[GroundItemManager] Pickup prevented because the inventory is full.");
            }

            var tile = CancelActivePickupRoutine();
            RefreshMenuIfTrackingTile(tile);
        }

        private Vector2Int CancelActivePickupRoutine()
        {
            if (activePickupCoroutine != null)
            {
                StopCoroutine(activePickupCoroutine);
                activePickupCoroutine = null;
            }

            var tile = activePickupTile;
            activePickup = null;
            activePickupTile = default;
            return tile;
        }

        private void AbortActivePickup(string reason)
        {
            if (enableDebugLogging)
                Debug.Log($"[GroundItemManager] Pickup aborted: {reason}");
            CancelActivePickupRoutine();
        }

        private void HandlePickupUnavailable()
        {
            var tile = activePickupTile;
            FloatingText.Show("That item is no longer available.", activePickupLastKnownPosition);
            AbortActivePickup("Pickup despawned during approach.");
            RefreshMenuIfTrackingTile(tile);
        }
    }
}
