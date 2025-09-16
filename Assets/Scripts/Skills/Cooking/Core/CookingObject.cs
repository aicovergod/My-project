using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Inventory;
using Player;
using UI;
using Pets;
using Skills.Common;

namespace Skills.Cooking
{
    /// <summary>
    /// World object that allows the player to cook items when used.
    /// The player selects a cookable item in the inventory and then
    /// clicks this object to begin cooking. Cooking stops if the
    /// player moves or steps too far away.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class CookingObject : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private float cancelDistance = 3f;

        private static Dictionary<string, CookableRecipe> recipeLookup;

        private Inventory.Inventory inventory;
        private CookingSkill cookingSkill;
        private PlayerMover playerMover;
        private Transform playerTransform;

        private void Awake()
        {
            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                inventory = playerObj.GetComponent<Inventory.Inventory>();
                cookingSkill = playerObj.GetComponent<CookingSkill>();
                playerMover = playerObj.GetComponent<PlayerMover>();
                playerTransform = playerObj.transform;
            }
            EnsureRecipeLookup();
            var mainCam = Camera.main;
            if (mainCam != null && mainCam.GetComponent<Physics2DRaycaster>() == null)
                mainCam.gameObject.AddComponent<Physics2DRaycaster>();
        }

        private void EnsureRecipeLookup()
        {
            if (recipeLookup != null)
                return;
            recipeLookup = new Dictionary<string, CookableRecipe>();
            var recipes = Resources.LoadAll<CookableRecipe>("CookingDatabase");
            foreach (var r in recipes)
            {
                if (r != null && !string.IsNullOrEmpty(r.rawItemId))
                    recipeLookup[r.rawItemId] = r;
            }
        }

        private void Update()
        {
            if (cookingSkill != null && cookingSkill.IsCooking)
            {
                if (playerMover != null && playerMover.IsMoving)
                {
                    cookingSkill.StopCooking();
                    return;
                }
                if (playerTransform != null && Vector3.Distance(playerTransform.position, transform.position) > cancelDistance)
                    cookingSkill.StopCooking();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                TryStartCooking();
            }
        }

        private void TryStartCooking()
        {
            if (inventory == null || cookingSkill == null)
                return;

            int selected = inventory.selectedIndex;
            if (selected < 0)
                return;

            var entry = inventory.GetSlot(selected);
            if (entry.item == null)
                return;

            if (!recipeLookup.TryGetValue(entry.item.id, out var recipe))
            {
                FloatingText.Show("You can't cook that", transform.position, null, GatheringRewardProcessor.DefaultFloatingTextSize);
                return;
            }

            if (cookingSkill.Level < recipe.requiredLevel)
            {
                FloatingText.Show($"You need Cooking level {recipe.requiredLevel}", transform.position, null, GatheringRewardProcessor.DefaultFloatingTextSize);
                return;
            }

            int quantity = inventory.GetItemCount(entry.item);
            if (quantity <= 0)
                return;

            cookingSkill.StartCooking(recipe, quantity);
            inventory.ClearSelection();
        }
    }
}

