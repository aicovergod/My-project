using UnityEngine;
using Inventory;
using Player;

namespace Combat
{
    /// <summary>
    /// Displays the player's current weapon sprite above the engaged enemy.
    /// </summary>
    public class CombatHUD : MonoBehaviour
    {
        private GameObject weaponRoot;
        private SpriteRenderer weaponRenderer;
        private Enemy currentEnemy;
        private readonly Vector3 offset = Vector3.zero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            var go = new GameObject("CombatHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<CombatHUD>();
        }

        private void Awake()
        {
            var manager = CombatManager.Instance;
            if (manager != null)
            {
                manager.OnCombatStarted += HandleStart;
                manager.OnCombatEnded += HandleStop;
            }
            CreateWeaponSprite();
        }

        private void CreateWeaponSprite()
        {
            weaponRoot = new GameObject("CombatWeapon");
            weaponRoot.transform.SetParent(transform);
            weaponRenderer = weaponRoot.AddComponent<SpriteRenderer>();
            weaponRenderer.sortingOrder = 100;
            weaponRoot.SetActive(false);
        }

        private void HandleStart(PlayerCombat player, Enemy enemy)
        {
            currentEnemy = enemy;
            Sprite weaponSprite = null;

            if (player != null)
            {
                var equipment = player.GetComponent<Equipment>();
                if (equipment != null)
                {
                    var entry = equipment.GetEquipped(EquipmentSlot.Weapon);
                    if (entry.item != null && entry.item.icon != null)
                        weaponSprite = entry.item.icon;
                }
            }

            if (weaponSprite != null)
            {
                weaponRenderer.sprite = weaponSprite;
                weaponRoot.SetActive(true);
            }
        }

        private void HandleStop(PlayerCombat player, Enemy enemy)
        {
            currentEnemy = null;
            if (weaponRoot != null)
            {
                weaponRoot.SetActive(false);
                if (weaponRenderer != null)
                    weaponRenderer.sprite = null;
            }
        }

        private void Update()
        {
            if (currentEnemy != null && weaponRoot != null && weaponRoot.activeSelf)
                weaponRoot.transform.position = currentEnemy.transform.position + offset;
        }

        private void OnDestroy()
        {
            var manager = CombatManager.Instance;
            if (manager != null)
            {
                manager.OnCombatStarted -= HandleStart;
                manager.OnCombatEnded -= HandleStop;
            }
        }
    }
}
