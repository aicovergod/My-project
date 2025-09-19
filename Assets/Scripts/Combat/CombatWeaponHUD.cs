using UnityEngine;
using Inventory;
using UI;
using World;

namespace Combat
{
    /// <summary>
    /// Displays the player's current weapon sprite above the active combat target.
    /// </summary>
    public class CombatWeaponHUD : MonoBehaviour
    {
        public static CombatWeaponHUD Instance => PersistentSceneSingleton<CombatWeaponHUD>.Instance;

        private CombatController controller;
        private Equipment equipment;
        private Transform target;
        private GameObject weaponRoot;
        private SpriteRenderer weaponRenderer;
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);
        private bool spellActiveLastFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            PersistentSceneSingleton<CombatWeaponHUD>.Bootstrap(CreateSingleton);
        }

        private static CombatWeaponHUD CreateSingleton()
        {
            var go = new GameObject(nameof(CombatWeaponHUD));
            return go.AddComponent<CombatWeaponHUD>();
        }

        private void Awake()
        {
            if (!PersistentSceneSingleton<CombatWeaponHUD>.HandleAwake(this))
                return;

            controller = FindObjectOfType<CombatController>();
            if (controller != null)
                controller.OnCombatTargetChanged += HandleTargetChanged;
            if (controller != null)
                equipment = controller.GetComponent<Equipment>();
            if (equipment != null)
                equipment.OnEquipmentChanged += HandleEquipmentChanged;
            CreateWeaponSprite();
        }

        private void CreateWeaponSprite()
        {
            weaponRoot = new GameObject("CombatWeaponSprite");
            weaponRoot.transform.SetParent(transform);
            weaponRenderer = weaponRoot.AddComponent<SpriteRenderer>();
            weaponRenderer.sortingOrder = 100;
            weaponRoot.SetActive(false);
        }

        private void HandleTargetChanged(CombatTarget newTarget)
        {
            target = newTarget != null ? newTarget.transform : null;
            RefreshWeaponSprite();
        }

        private void Update()
        {
            bool spellActive = MagicUI.ActiveSpell != null;
            if (spellActive != spellActiveLastFrame)
            {
                RefreshWeaponSprite();
                spellActiveLastFrame = spellActive;
            }

            if (target != null && weaponRoot != null && weaponRoot.activeSelf)
                weaponRoot.transform.position = target.position + offset;
        }

        private void OnDestroy()
        {
            if (controller != null)
                controller.OnCombatTargetChanged -= HandleTargetChanged;
            if (equipment != null)
                equipment.OnEquipmentChanged -= HandleEquipmentChanged;

            PersistentSceneSingleton<CombatWeaponHUD>.HandleOnDestroy(this);
        }

        private void HandleEquipmentChanged(EquipmentSlot slot)
        {
            if (slot != EquipmentSlot.Weapon)
                return;

            RefreshWeaponSprite();
        }

        private void RefreshWeaponSprite()
        {
            if (weaponRenderer == null || weaponRoot == null)
                return;

            if (target == null || MagicUI.ActiveSpell != null)
            {
                weaponRenderer.sprite = null;
                weaponRoot.SetActive(false);
                return;
            }

            var entry = equipment != null ? equipment.GetEquipped(EquipmentSlot.Weapon) : default;
            if (entry.item != null && entry.item.icon != null)
            {
                weaponRenderer.sprite = entry.item.icon;
                weaponRoot.SetActive(true);
            }
            else
            {
                weaponRenderer.sprite = null;
                weaponRoot.SetActive(false);
            }
        }
    }
}
