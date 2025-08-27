using UnityEngine;
using Inventory;

namespace Combat
{
    /// <summary>
    /// Displays the player's current weapon sprite above the active combat target.
    /// </summary>
    public class CombatWeaponHUD : MonoBehaviour
    {
        private CombatController controller;
        private Equipment equipment;
        private Transform target;
        private GameObject weaponRoot;
        private SpriteRenderer weaponRenderer;
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            if (UnityEngine.Object.FindObjectOfType<CombatWeaponHUD>() != null)
                return;

            var go = new GameObject("CombatWeaponHUD");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<CombatWeaponHUD>();
        }

        private void Awake()
        {
            controller = FindObjectOfType<CombatController>();
            if (controller != null)
                controller.OnCombatTargetChanged += HandleTargetChanged;
            if (controller != null)
                equipment = controller.GetComponent<Equipment>();
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
            if (newTarget != null)
            {
                target = newTarget.transform;
                var entry = equipment != null ? equipment.GetEquipped(EquipmentSlot.Weapon) : default;
                if (entry.item != null && entry.item.icon != null)
                {
                    weaponRenderer.sprite = entry.item.icon;
                    weaponRoot.SetActive(true);
                }
            }
            else
            {
                target = null;
                if (weaponRenderer != null)
                    weaponRenderer.sprite = null;
                if (weaponRoot != null)
                    weaponRoot.SetActive(false);
            }
        }

        private void Update()
        {
            if (target != null && weaponRoot != null && weaponRoot.activeSelf)
                weaponRoot.transform.position = target.position + offset;
        }

        private void OnDestroy()
        {
            if (controller != null)
                controller.OnCombatTargetChanged -= HandleTargetChanged;
        }
    }
}
