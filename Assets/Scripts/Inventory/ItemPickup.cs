using Inventory.GroundItems;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Inventory
{
    /// <summary>
    /// World-space representation of a dropped item. Registers with <see cref="GroundItemManager"/>
    /// for OSRS-style pickup behaviour and optionally supports legacy walk-over collection when
    /// <see cref="enableContactPickup"/> is enabled.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class ItemPickup : MonoBehaviour, IPointerClickHandler
    {
        [Header("Item")]
        [SerializeField]
        [Tooltip("Item granted when the pickup is collected.")]
        private ItemData item;

        [SerializeField]
        [Tooltip("Stack size granted when the pickup is collected.")]
        private int amount = 1;

        [SerializeField]
        [Tooltip("Sprite renderer that displays the item icon.")]
        private SpriteRenderer iconRenderer;

        [Header("Lifetime")]
        [SerializeField]
        [Tooltip("Seconds before the pickup automatically despawns if untouched.")]
        private float lifetime = 60f;

        [Header("Pickup Behaviour")]
        [SerializeField]
        [Tooltip("If enabled the item can still be collected by walking over it.")]
        private bool enableContactPickup;

        [SerializeField]
        [Tooltip("Radius used when checking whether the player is close enough to collect the item.")]
        private float pickupRadius = 0.2f;

        private GroundItemManager manager;
        private Vector2Int cachedTile;
        private bool registeredWithManager;

        /// <summary>Spawn order assigned by <see cref="GroundItemManager"/> for deterministic menus.</summary>
        internal long SpawnOrder { get; set; }

        /// <summary>Reference to the item this pickup represents.</summary>
        public ItemData Item => item;

        /// <summary>Quantity granted when the pickup is collected.</summary>
        public int Amount => amount;

        /// <summary>Radius used when validating pickup proximity.</summary>
        public float PickupRadius => pickupRadius;

        /// <summary>Tile coordinate cached for this pickup.</summary>
        public Vector2Int TileCoordinate => cachedTile;

        private void Reset()
        {
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
            Initialize(item, amount);
        }

        private void Awake()
        {
            iconRenderer ??= GetComponentInChildren<SpriteRenderer>();
            cachedTile = GroundItemManager.GetTileForPosition(transform.position);
            manager = GroundItemManager.Instance;
        }

        private void OnEnable()
        {
            RegisterWithManager();
        }

        private void Start()
        {
            Initialize(item, amount);
            Destroy(gameObject, lifetime);
        }

        private void OnValidate()
        {
            Initialize(item, amount);
            cachedTile = GroundItemManager.GetTileForPosition(transform.position);
        }

        private void OnDisable()
        {
            UnregisterFromManager();
        }

        /// <summary>Updates icon visuals and cached data for the pickup.</summary>
        public void Initialize(ItemData newItem, int newAmount)
        {
            item = newItem;
            amount = Mathf.Max(1, newAmount);
            iconRenderer ??= GetComponentInChildren<SpriteRenderer>();

            if (iconRenderer != null && item != null)
                iconRenderer.sprite = item.icon;
        }

        /// <summary>Called by the manager to cache the latest tile coordinate.</summary>
        internal void CacheTile(Vector2Int tile)
        {
            cachedTile = tile;
        }

        /// <summary>Attempts to add the item to the supplied inventory.</summary>
        public bool TryCollect(Inventory inventory)
        {
            if (inventory == null || item == null || amount <= 0)
                return false;

            if (inventory.AddItem(item, amount))
            {
                Destroy(gameObject);
                return true;
            }

            return false;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!enableContactPickup)
                return;

            if (!other.CompareTag("Player"))
                return;

            var inventory = other.GetComponent<Inventory>();
            if (inventory == null)
                return;

            if (!TryCollect(inventory))
            {
                FloatingText.Show("Inventory full", transform.position);
            }
        }

        /// <summary>Handles pointer clicks routed by the Physics2DRaycaster.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null)
                return;

            manager ??= GroundItemManager.Instance;
            if (manager == null)
                manager = FindObjectOfType<GroundItemManager>();

            manager?.HandlePickupClick(this, eventData.position, eventData.button);
        }

        private void RegisterWithManager()
        {
            if (registeredWithManager)
                return;

            manager ??= GroundItemManager.Instance;
            if (manager == null)
                manager = FindObjectOfType<GroundItemManager>();

            if (manager == null)
                return;

            manager.RegisterPickup(this);
            registeredWithManager = true;
        }

        private void UnregisterFromManager()
        {
            if (!registeredWithManager)
                return;

            manager ??= GroundItemManager.Instance;
            if (manager == null)
                manager = FindObjectOfType<GroundItemManager>();

            manager?.UnregisterPickup(this);
            registeredWithManager = false;
        }
    }
}

