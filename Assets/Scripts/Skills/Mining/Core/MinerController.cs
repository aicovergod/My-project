using UnityEngine;
using Inventory;
using Player;

namespace Skills.Mining
{
    /// <summary>
    /// Handles player input and range checks for mining.
    /// </summary>
    [DisallowMultipleComponent]
    public class MinerController : MonoBehaviour
    {
        [SerializeField] private float interactRange = 1.5f;
        [SerializeField] private float cancelDistance = 3f;
        [SerializeField] private LayerMask rockMask = ~0;

        [Header("References")]
        [SerializeField] private MiningSkill miningSkill;
        [SerializeField] private PickaxeToUse pickaxeSelector;
        [SerializeField] private PlayerMover playerMover;

        private MineableRock nearbyRock;

        private Camera cam;

        private void Awake()
        {
            if (miningSkill == null)
                miningSkill = GetComponent<MiningSkill>();
            if (pickaxeSelector == null)
                pickaxeSelector = GetComponent<PickaxeToUse>();
            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();
            cam = Camera.main;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                var rock = GetRockUnderCursor();
                if (rock != null)
                    TryStartMining(rock);
            }
            else if (Input.GetMouseButtonDown(1))
            {
                var rock = GetRockUnderCursor();
                if (rock != null)
                    rock.Prospect(transform);
            }

            if (Input.GetKeyDown(KeyCode.Escape))
                miningSkill.StopMining();

            if (nearbyRock != null && Input.GetKeyDown(KeyCode.E))
                TryStartMining(nearbyRock);

            if (miningSkill.IsMining)
            {
                if (playerMover != null && playerMover.IsMoving)
                    miningSkill.StopMining();
                else if (Vector3.Distance(transform.position, miningSkill.CurrentRock.transform.position) > cancelDistance)
                    miningSkill.StopMining();
            }
        }

        private MineableRock GetRockUnderCursor()
        {
            if (cam == null)
                cam = Camera.main;
            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            var hit = Physics2D.Raycast(world, Vector2.zero, 0f, rockMask);
            if (hit.collider != null)
                return hit.collider.GetComponent<MineableRock>();
            return null;
        }

        private void TryStartMining(MineableRock rock)
        {
            if (rock == null || rock.IsDepleted)
                return;

            float dist = Vector3.Distance(transform.position, rock.transform.position);
            if (dist > interactRange)
                return;

            var pickaxe = pickaxeSelector.GetBestPickaxe();
            if (pickaxe == null)
            {
                FloatingText.Show("You need a pickaxe", transform.position);
                return;
            }
            if (miningSkill.Level < rock.RockDef.Ore.LevelRequirement)
            {
                FloatingText.Show($"You need Mining level {rock.RockDef.Ore.LevelRequirement}", transform.position);
                return;
            }
            if (pickaxe.Tier < rock.RockDef.RequiresToolTier)
            {
                FloatingText.Show("You need a better pickaxe", transform.position);
                return;
            }

            if (!miningSkill.CanAddOre(rock.RockDef.Ore))
            {
                FloatingText.Show("Your inventory is full", transform.position);
                return;
            }

            miningSkill.StartMining(rock, pickaxe);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var rock = other.GetComponent<MineableRock>();
            if (rock != null)
                nearbyRock = rock;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var rock = other.GetComponent<MineableRock>();
            if (rock != null && rock == nearbyRock)
                nearbyRock = null;
        }
    }
}
