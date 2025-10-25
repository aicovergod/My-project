/// Feature: Enabled companion pickup context menu actions for ground drops.
using System;
using System.Collections;
using System.Collections.Generic;
using Companions;
using Core;
using Player;
using Pets;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using World;
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

            RefreshWorldCamera(worldCamera);

            RebindDependencies();
        }

        private void OnEnable()
        {
            GameManager.ServicesReady += OnPersistentServicesReady;
            SceneTransitionManager.TransitionCompleted += OnSceneTransitionCompleted;
        }

        private void OnDisable()
        {
            GameManager.ServicesReady -= OnPersistentServicesReady;
            SceneTransitionManager.TransitionCompleted -= OnSceneTransitionCompleted;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>Rebinds cached references to the player mover and inventory without scanning the entire scene.</summary>
        public void RebindDependencies()
        {
            bool moverInvalid = playerMover == null || !playerMover.gameObject.activeInHierarchy || !playerMover.isActiveAndEnabled;

            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            Transform playerTransform = playerObject != null ? playerObject.transform : null;

            bool inventoryInvalid = !IsPlayerInventoryCandidate(playerInventory, playerTransform);

            if (!moverInvalid && !inventoryInvalid)
                return;

            if (playerObject == null)
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[GroundItemManager] Unable to locate the player object while rebinding dependencies.");

                if (moverInvalid)
                    playerMover = null;

                if (inventoryInvalid)
                    playerInventory = null;

                return;
            }

            if (moverInvalid)
            {
                var resolvedMover = playerObject.GetComponent<PlayerMover>();
                if (resolvedMover != null && resolvedMover.isActiveAndEnabled && resolvedMover.gameObject.activeInHierarchy)
                {
                    playerMover = resolvedMover;
                    if (enableDebugLogging)
                        Debug.Log("[GroundItemManager] Rebound PlayerMover reference from player object.");
                }
                else
                {
                    playerMover = resolvedMover;
                    if (enableDebugLogging)
                        Debug.LogWarning("[GroundItemManager] PlayerMover component was not available or active during rebind.");
                }
            }

            if (inventoryInvalid)
            {
                InventoryComponent resolvedInventory = playerObject.GetComponent<InventoryComponent>();
                if (!IsPlayerInventoryCandidate(resolvedInventory, playerTransform))
                {
                    resolvedInventory = null;
                }

                if (resolvedInventory == null)
                {
                    resolvedInventory = playerObject.GetComponentInChildren<InventoryComponent>(true);
                    if (!IsPlayerInventoryCandidate(resolvedInventory, playerTransform))
                        resolvedInventory = null;
                }

                if (resolvedInventory == null)
                {
                    var allInventories = FindObjectsOfType<InventoryComponent>(true);
                    foreach (var candidate in allInventories)
                    {
                        if (candidate == null)
                            continue;

                        if (candidate.GetComponent<PetStorage>() != null)
                            continue;

                        if (!IsPlayerInventoryCandidate(candidate, playerTransform))
                            continue;

                        resolvedInventory = candidate;
                        break;
                    }
                }

                if (resolvedInventory != null)
                {
                    playerInventory = resolvedInventory;
                    if (enableDebugLogging)
                        Debug.Log($"[GroundItemManager] Rebound Inventory reference from '{playerInventory.gameObject.name}'.");
                }
                else
                {
                    playerInventory = null;
                    if (enableDebugLogging)
                        Debug.LogWarning("[GroundItemManager] Inventory component was not found on the player during rebind.");
                }
            }
        }

        private static bool IsPlayerInventoryCandidate(InventoryComponent inventory, Transform playerTransform)
        {
            if (inventory == null || playerTransform == null)
                return false;

            if (!inventory.isActiveAndEnabled || !inventory.gameObject.activeInHierarchy)
                return false;

            Transform inventoryTransform = inventory.transform;
            return inventoryTransform == playerTransform || inventoryTransform.IsChildOf(playerTransform);
        }

        private void OnPersistentServicesReady()
        {
            RebindDependencies();
            RefreshWorldCamera();
        }

        private void OnSceneTransitionCompleted()
        {
            RebindDependencies();
            RefreshWorldCamera();
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
                if (pickup.IsBeingCollected)
                {
                    if (enableDebugLogging)
                        Debug.Log("[GroundItemManager] Pickup removed as part of a successful collection. Skipping abort.");
                }
                else
                {
                    AbortActivePickup("Pickup was removed before collection could complete.");
                }
            }
        }

        /// <summary>
        /// Processes a pointer click on a ground item, dispatching between left/right behaviour to
        /// mirror OSRS interactions (left: quick-take, right: show context menu).
        /// </summary>
        /// <param name="pickup">Pickup that received the pointer event.</param>
        /// <param name="clickScreenPosition">Screen position of the pointer event.</param>
        /// <param name="button">Button that triggered the pointer event.</param>
        public void HandlePickupClick(ItemPickup pickup, Vector3 clickScreenPosition, PointerEventData.InputButton button)
        {
            if (pickup == null)
                return;

            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
                RefreshWorldCamera();

            Vector2Int tile = pickup.TileCoordinate;
            var pickups = GetPickupsOnTile(tile);

            if (pickups.Count == 0)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"[GroundItemManager] No pickups registered on tile {tile} for clicked item {pickup.name}.");
                HideMenu();
                return;
            }

            switch (button)
            {
                case PointerEventData.InputButton.Left:
                    if (pickups.Count == 1)
                    {
                        HideMenu();
                        BeginPickupRoutine(pickups[0]);
                        return;
                    }

                    ShowMenu(tile, pickups, clickScreenPosition);
                    break;
                case PointerEventData.InputButton.Right:
                    ShowMenu(tile, pickups, clickScreenPosition);
                    break;
                default:
                    if (enableDebugLogging)
                        Debug.Log($"[GroundItemManager] Ignored unsupported pointer button {button} for pickup {pickup.name}.");
                    break;
            }
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
            if (playerMover != null && playerMover.isActiveAndEnabled && playerMover.gameObject.activeInHierarchy)
                return true;

            if (enableDebugLogging)
                Debug.LogWarning("[GroundItemManager] PlayerMover reference missing or inactive; attempting to rebind.");

            RebindDependencies();

            if (playerMover != null && playerMover.isActiveAndEnabled && playerMover.gameObject.activeInHierarchy)
                return true;

            Debug.LogWarning("[GroundItemManager] PlayerMover reference missing or inactive; cannot auto-pickup.");
            return false;
        }

        private IReadOnlyList<ItemPickup> GetPickupsOnTile(Vector2Int tile)
        {
            if (!pickupsByTile.TryGetValue(tile, out var list))
                return Array.Empty<ItemPickup>();

            // Remove any stale references before building the outgoing list.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null)
                    list.RemoveAt(i);
            }

            if (list.Count == 0)
            {
                pickupsByTile.Remove(tile);
                return Array.Empty<ItemPickup>();
            }

            SortBySpawnOrder(list);

            return list;
        }

        private void SortBySpawnOrder(List<ItemPickup> pickups)
        {
            // Sort newest-first so index zero always represents the most recent spawn. Null entries
            // are pushed to the back so any lingering stale references never surface in the UI.
            pickups.Sort((a, b) =>
            {
                if (ReferenceEquals(a, b))
                    return 0;

                if (a == null)
                    return 1;

                if (b == null)
                    return -1;

                return b.SpawnOrder.CompareTo(a.SpawnOrder);
            });
        }

        private void ShowMenu(Vector2Int tile, IReadOnlyList<ItemPickup> pickups, Vector2 screenPosition)
        {
            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
                RefreshWorldCamera();

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

        /// <summary>
        /// Resolves the active world camera, ensuring a Physics2DRaycaster is present so ground item clicks succeed.
        /// </summary>
        /// <param name="candidate">Optional camera hint supplied by callers that may already be valid.</param>
        private void RefreshWorldCamera(Camera candidate = null)
        {
            Camera resolvedCamera = null;

            if (candidate != null && candidate.isActiveAndEnabled)
            {
                resolvedCamera = candidate;
            }
            else if (worldCamera != null && worldCamera.isActiveAndEnabled)
            {
                resolvedCamera = worldCamera;
            }
            else
            {
                Camera mainCamera = Camera.main;
                if (mainCamera != null && mainCamera.isActiveAndEnabled)
                {
                    resolvedCamera = mainCamera;
                }
                else
                {
                    var allCameras = Camera.allCameras;
                    for (int i = 0; i < allCameras.Length; i++)
                    {
                        Camera camera = allCameras[i];
                        if (camera != null && camera.isActiveAndEnabled)
                        {
                            resolvedCamera = camera;
                            break;
                        }
                    }
                }
            }

            bool cameraChanged = worldCamera != resolvedCamera;
            worldCamera = resolvedCamera;

            if (cameraChanged)
            {
                if (enableDebugLogging)
                {
                    if (worldCamera != null)
                    {
                        Debug.Log($"[GroundItemManager] Bound world camera to {worldCamera.name}.");
                    }
                    else
                    {
                        Debug.LogWarning("[GroundItemManager] World camera reference cleared; no active cameras detected.");
                    }
                }
            }

            EnsurePhysicsRaycaster();
        }

        private void OnMenuSelection(ItemPickup pickup, PointerEventData.InputButton button)
        {
            HideMenu();
            if (pickup == null)
                return;

            if (button == PointerEventData.InputButton.Right)
            {
                var drop = WorldDrop.FromPickup(pickup);
                if (drop != null)
                    CompanionPickupService.RequestPickup(drop);
                return;
            }

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
            {
                if (enableDebugLogging)
                    Debug.LogWarning("[GroundItemManager] Player inventory reference missing; attempting to rebind before pickup.");

                RebindDependencies();

                if (playerInventory == null)
                {
                    if (enableDebugLogging)
                        Debug.LogWarning("[GroundItemManager] Pickup failed because no Inventory component was located after rebinding.");
                    CancelActivePickupRoutine();
                    return;
                }
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
