using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Inventory;
using Player;
using UI;
using Pets;

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
        private bool isFryingPan;

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

            var petExp = GetComponent<PetExperience>();
            var definition = petExp?.definition;
            isFryingPan = definition != null && definition.id == "Mr Frying Pan";

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
                if (!isFryingPan && playerMover != null && playerMover.IsMoving)
                {
                    cookingSkill.StopCooking();
                    return;
                }
                if (!isFryingPan && playerTransform != null && Vector3.Distance(playerTransform.position, transform.position) > cancelDistance)
                    cookingSkill.StopCooking();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (isFryingPan)
                    StartCoroutine(AutoCookAll());
                else
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
                FloatingText.Show("You can't cook that", transform.position);
                return;
            }

            if (cookingSkill.Level < recipe.requiredLevel)
            {
                FloatingText.Show($"You need Cooking level {recipe.requiredLevel}", transform.position);
                return;
            }

            int quantity = inventory.GetItemCount(entry.item);
            if (quantity <= 0)
                return;

            cookingSkill.StartCooking(recipe, quantity);
            inventory.ClearSelection();
        }

        private IEnumerator AutoCookAll()
        {
            if (inventory == null || cookingSkill == null)
                yield break;

            for (int i = 0; i < inventory.size; i++)
            {
                var entry = inventory.GetSlot(i);
                var item = entry.item;
                if (item == null)
                    continue;
                if (!recipeLookup.TryGetValue(item.id, out var recipe))
                    continue;
                if (cookingSkill.Level < recipe.requiredLevel)
                    continue;

                int quantity = inventory.GetItemCount(item);
                if (quantity <= 0)
                    continue;

                cookingSkill.StartCooking(recipe, quantity);

                while (cookingSkill.IsCooking)
                    yield return null;
            }
            inventory.ClearSelection();
        }
    }
}

