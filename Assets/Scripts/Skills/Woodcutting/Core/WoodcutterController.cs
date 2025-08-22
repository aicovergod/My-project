using UnityEngine;
using UnityEngine.EventSystems;
using Inventory;
using Player;
using Skills.Mining;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Handles player input and range checks for woodcutting.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodcutterController : MonoBehaviour
    {
        [SerializeField] private float interactRange = 1.5f;
        [SerializeField] private float cancelDistance = 3f;
        [SerializeField] [Tooltip("Layers including tree interaction triggers")] private LayerMask treeMask = ~0;

        [Header("References")]
        [SerializeField] private WoodcuttingSkill woodcuttingSkill;
        [SerializeField] private AxeToUse axeSelector;
        [SerializeField] private PlayerMover playerMover;

        private TreeNode nearbyTree;
        private Camera cam;

        private void Awake()
        {
            if (woodcuttingSkill == null)
                woodcuttingSkill = GetComponent<WoodcuttingSkill>();
            if (axeSelector == null)
                axeSelector = GetComponent<AxeToUse>();
            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();
            cam = Camera.main;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                {
                    var tree = GetTreeUnderCursor();
                    if (tree != null)
                        TryStartChopping(tree);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
                woodcuttingSkill.StopChopping();

            if (nearbyTree != null && Input.GetKeyDown(KeyCode.E))
                TryStartChopping(nearbyTree);

            if (woodcuttingSkill.IsChopping)
            {
                if (playerMover != null && playerMover.IsMoving)
                    woodcuttingSkill.StopChopping();
                else if (Vector3.Distance(transform.position, woodcuttingSkill.CurrentTree.transform.position) > cancelDistance)
                    woodcuttingSkill.StopChopping();
                else if (woodcuttingSkill.CurrentTree.IsDepleted)
                    woodcuttingSkill.StopChopping();
            }
        }

        private TreeNode GetTreeUnderCursor()
        {
            if (cam == null)
                cam = Camera.main;
            Vector2 world = cam.ScreenToWorldPoint(Input.mousePosition);
            var collider = Physics2D.OverlapPoint(world, treeMask);
            if (collider != null)
                return collider.GetComponentInParent<TreeNode>();
            return null;
        }

        private void TryStartChopping(TreeNode tree)
        {
            if (tree == null || tree.IsDepleted || tree.IsBusy)
                return;

            float dist = Vector3.Distance(transform.position, tree.transform.position);
            if (dist > interactRange)
                return;

            var axe = axeSelector.GetBestAxe();
            if (axe == null)
            {
                FloatingText.Show("You need an axe", transform.position);
                return;
            }
            if (woodcuttingSkill.Level < tree.def.RequiredWoodcuttingLevel)
            {
                FloatingText.Show($"You need Woodcutting level {tree.def.RequiredWoodcuttingLevel}", transform.position);
                return;
            }
            if (woodcuttingSkill.Level < axe.RequiredWoodcuttingLevel)
            {
                FloatingText.Show($"You need Woodcutting level {axe.RequiredWoodcuttingLevel}", transform.position);
                return;
            }

            if (!woodcuttingSkill.CanAddLog(tree.def))
            {
                FloatingText.Show("Your inventory is full", transform.position);
                return;
            }

            woodcuttingSkill.StartChopping(tree, axe);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var tree = other.GetComponent<TreeNode>();
            if (tree != null)
                nearbyTree = tree;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var tree = other.GetComponent<TreeNode>();
            if (tree != null && tree == nearbyTree)
                nearbyTree = null;
        }
    }
}
