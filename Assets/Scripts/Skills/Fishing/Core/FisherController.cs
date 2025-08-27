using UnityEngine;
using UnityEngine.EventSystems;
using Inventory;
using Player;
using Skills.Mining;

namespace Skills.Fishing
{
    [DisallowMultipleComponent]
    public class FisherController : MonoBehaviour
    {
        [SerializeField] private float interactRange = 1.5f;
        [SerializeField] private float cancelDistance = 3f;
        [SerializeField] [Tooltip("Layers including fishing spots")] private LayerMask spotMask = ~0;

        [Header("References")]
        [SerializeField] private FishingSkill fishingSkill;
        [SerializeField] private FishingToolToUse toolSelector;
        [SerializeField] private PlayerMover playerMover;
        [SerializeField] private Animator animator;

        private FishableSpot nearbySpot;
        private Camera cam;

        private void Awake()
        {
            if (fishingSkill == null)
                fishingSkill = GetComponent<FishingSkill>();
            if (toolSelector == null)
                toolSelector = GetComponent<FishingToolToUse>();
            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            cam = Camera.main;
        }

        private void OnEnable()
        {
            if (fishingSkill != null)
                fishingSkill.OnStopFishing += HandleStop;
        }

        private void OnDisable()
        {
            if (fishingSkill != null)
                fishingSkill.OnStopFishing -= HandleStop;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                {
                    var spot = GetSpotUnderCursor();
                    if (spot != null)
                        TryStartFishing(spot);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
                fishingSkill.StopFishing();

            if (nearbySpot != null && Input.GetKeyDown(KeyCode.E))
                TryStartFishing(nearbySpot);

            if (fishingSkill.IsFishing)
            {
                if (playerMover != null && playerMover.IsMoving)
                    fishingSkill.StopFishing();
                else
                {
                    float cancelDist = fishingSkill.CurrentSpot != null && fishingSkill.CurrentSpot.def != null
                        ? fishingSkill.CurrentSpot.def.CancelDistance
                        : cancelDistance;
                    if (Vector3.Distance(transform.position, fishingSkill.CurrentSpot.transform.position) > cancelDist)
                        fishingSkill.StopFishing();
                    else if (fishingSkill.CurrentSpot.IsDepleted)
                        fishingSkill.StopFishing();
                }
            }
        }

        private FishableSpot GetSpotUnderCursor()
        {
            if (cam == null)
                cam = Camera.main;
            Vector2 world = cam.ScreenToWorldPoint(Input.mousePosition);
            var collider = Physics2D.OverlapPoint(world, spotMask);
            if (collider != null)
                return collider.GetComponentInParent<FishableSpot>();
            return null;
        }

        private void TryStartFishing(FishableSpot spot)
        {
            if (spot == null || spot.IsDepleted || spot.IsBusy)
                return;

            float dist = Vector3.Distance(transform.position, spot.transform.position);
            float range = spot.def != null ? spot.def.InteractRange : interactRange;
            if (dist > range)
                return;

            var tool = toolSelector.GetBestTool();
            if (tool == null)
            {
                FloatingText.Show("You need a fishing tool", transform.position);
                return;
            }
            if (fishingSkill.Level < tool.RequiredLevel)
            {
                FloatingText.Show($"You need Fishing level {tool.RequiredLevel}", transform.position);
                return;
            }

            FishDefinition catchable = null;
            int minLevel = int.MaxValue;
            foreach (var fish in spot.def.AvailableFish)
            {
                if (fish == null) continue;
                minLevel = Mathf.Min(minLevel, fish.RequiredLevel);
                if (fishingSkill.Level >= fish.RequiredLevel)
                    catchable = fish;
            }
            if (catchable == null)
            {
                FloatingText.Show($"You need Fishing level {minLevel}", transform.position);
                return;
            }
            if (!fishingSkill.CanAddFish(catchable))
            {
                FloatingText.Show("Your inventory is full", transform.position);
                return;
            }

            fishingSkill.StartFishing(spot, tool);
            if (animator != null)
                animator.SetBool("isFishing", true);
        }

        private void HandleStop()
        {
            if (animator != null)
                animator.SetBool("isFishing", false);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var spot = other.GetComponent<FishableSpot>();
            if (spot != null)
                nearbySpot = spot;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var spot = other.GetComponent<FishableSpot>();
            if (spot != null && spot == nearbySpot)
                nearbySpot = null;
        }
    }
}
