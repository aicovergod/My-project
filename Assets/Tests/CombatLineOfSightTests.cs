// These integration-style tests rely on Unity's play mode test runner. They are wrapped
// in UNITY_INCLUDE_TESTS so the file is excluded from player builds where the test
// assemblies are not available, preventing compile errors from missing UnityTest
// attributes when the Test Runner package is stripped.
#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using Combat;
using NPC;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Validates that the new line-of-sight checks prevent combat resolution when
/// a blocking collider sits between combatants, while still allowing attacks
/// when the path is clear.
/// </summary>
public class CombatLineOfSightTests
{
    private class DummyTarget : MonoBehaviour, CombatTarget
    {
        public bool alive = true;
        public bool IsAlive => alive;
        public DamageType PreferredDefenceType => DamageType.Melee;
        public int CurrentHP => 10;
        public int MaxHP => 10;
        public int ApplyDamage(int amount, DamageType type, SpellElement element, object source) => amount;
    }

    private class TestCombatController : CombatController
    {
        private static readonly FieldInfo ObstructionMaskField = typeof(CombatController)
            .GetField("obstructionMask", BindingFlags.Instance | BindingFlags.NonPublic);

        public bool AttackAttempted { get; private set; }

        protected new void Awake()
        {
            // Skip the base Awake initialisation so the test does not require the
            // full player combat setup. The tests configure the required state
            // manually through reflection where necessary.
        }

        protected override void ResolveAttack(CombatTarget target)
        {
            AttackAttempted = true;
        }

        public void ResetAttackFlag()
        {
            AttackAttempted = false;
        }

        public void SetObstructionMask(LayerMask mask)
        {
            ObstructionMaskField.SetValue(this, mask);
        }
    }

    private class TestNpcCombat : BaseNpcCombat
    {
        public bool AttackAttempted { get; private set; }

        protected override void ResolveAttack(CombatTarget target)
        {
            AttackAttempted = true;
        }

        public void ResetAttackFlag()
        {
            AttackAttempted = false;
        }

        public void SetObstructionMask(LayerMask mask)
        {
            obstructionMask = mask;
        }
    }

    private static int EnsureLayer(string preferred)
    {
        int layer = LayerMask.NameToLayer(preferred);
        return layer >= 0 ? layer : 0;
    }

    private static LayerMask BuildMaskForLayer(int layer)
    {
        return 1 << layer;
    }

    private static GameObject CreateWall(int layer, Vector3 position)
    {
        var wall = new GameObject("TestWall");
        wall.layer = layer;
        var collider = wall.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.25f, 1f);
        wall.transform.position = position;
        return wall;
    }

    private static DummyTarget CreateTarget(Vector3 position)
    {
        var targetGo = new GameObject("Target");
        var target = targetGo.AddComponent<DummyTarget>();
        targetGo.transform.position = position;
        return target;
    }

    private static TestCombatController CreatePlayerController(LayerMask mask)
    {
        var go = new GameObject("PlayerCombatController");
        var controller = go.AddComponent<TestCombatController>();
        controller.SetObstructionMask(mask);
        return controller;
    }

    private static TestNpcCombat CreateNpcController(LayerMask mask, out GameObject spawner)
    {
        // Ensure a ground item spawner exists so the NpcDropper dependency
        // initialised by NpcCombatant can resolve without warnings.
        spawner = new GameObject("GroundItemSpawner");
        spawner.AddComponent<MyGame.Drops.GroundItemSpawner>();

        // Provide a shared hitsplat library so NpcCombatant does not log an error
        // during Awake when it attempts to cache its visual references.
        var hitsplatLibrary = ScriptableObject.CreateInstance<HitSplatLibrary>();
        var sharedField = typeof(NpcCombatant).GetField("sharedHitSplatLibrary",
            BindingFlags.Static | BindingFlags.NonPublic);
        sharedField.SetValue(null, hitsplatLibrary);

        var npcGo = new GameObject("NpcCombatRoot");
        var npc = npcGo.AddComponent<TestNpcCombat>();
        npc.SetObstructionMask(mask);
        return npc;
    }

    [UnityTest]
    public IEnumerator PlayerLineOfSight_AllowsAttackWhenClear()
    {
        int obstacleLayer = EnsureLayer("Obstacles");
        var mask = BuildMaskForLayer(obstacleLayer);
        var controller = CreatePlayerController(mask);
        var target = CreateTarget(new Vector3(0f, 1.5f, 0f));

        bool started = controller.TryAttackTarget(target);
        Assert.IsTrue(started, "Attack should start when nothing blocks the path.");

        // Allow the coroutine a frame to invoke the overridden ResolveAttack.
        yield return null;
        Assert.IsTrue(controller.AttackAttempted, "Attack coroutine should resolve when line-of-sight is clear.");

        Object.DestroyImmediate(controller.gameObject);
        Object.DestroyImmediate(target.gameObject);
    }

    [UnityTest]
    public IEnumerator PlayerLineOfSight_BlocksAttackWhenObstructed()
    {
        int obstacleLayer = EnsureLayer("Obstacles");
        var mask = BuildMaskForLayer(obstacleLayer);
        var controller = CreatePlayerController(mask);
        var target = CreateTarget(new Vector3(0f, 1.5f, 0f));
        var wall = CreateWall(obstacleLayer, new Vector3(0f, 0.75f, 0f));

        bool started = controller.TryAttackTarget(target);
        Assert.IsFalse(started, "Attack should be rejected when geometry blocks line-of-sight.");
        Assert.IsFalse(controller.AttackAttempted, "ResolveAttack should not run while an obstruction exists.");

        Object.DestroyImmediate(controller.gameObject);
        Object.DestroyImmediate(target.gameObject);
        Object.DestroyImmediate(wall);
    }

    [UnityTest]
    public IEnumerator NpcLineOfSight_AllowsAttackWhenClear()
    {
        int obstacleLayer = EnsureLayer("Obstacles");
        var mask = BuildMaskForLayer(obstacleLayer);
        var npc = CreateNpcController(mask, out var spawner);
        var target = CreateTarget(new Vector3(0f, 1.5f, 0f));

        npc.BeginAttacking(target);

        int frames = 0;
        const int maxFrames = 120;
        while (!npc.AttackAttempted && frames < maxFrames)
        {
            frames++;
            yield return null;
        }

        Assert.IsTrue(npc.AttackAttempted, "NPC should resolve an attack when the path is unobstructed.");

        Object.DestroyImmediate(npc.gameObject);
        Object.DestroyImmediate(target.gameObject);
        Object.DestroyImmediate(spawner);
    }

    [UnityTest]
    public IEnumerator NpcLineOfSight_BlocksAttackWhenObstructed()
    {
        int obstacleLayer = EnsureLayer("Obstacles");
        var mask = BuildMaskForLayer(obstacleLayer);
        var npc = CreateNpcController(mask, out var spawner);
        var target = CreateTarget(new Vector3(0f, 1.5f, 0f));
        var wall = CreateWall(obstacleLayer, new Vector3(0f, 0.75f, 0f));

        npc.BeginAttacking(target);

        int frames = 0;
        const int maxFrames = 120;
        while (frames < maxFrames)
        {
            frames++;
            yield return null;
            Assert.IsFalse(npc.AttackAttempted, "ResolveAttack should be skipped while the wall blocks the target.");
        }

        Assert.IsFalse(npc.AttackAttempted, "NPC should not attack a target hidden behind an obstruction.");

        Object.DestroyImmediate(npc.gameObject);
        Object.DestroyImmediate(target.gameObject);
        Object.DestroyImmediate(wall);
        Object.DestroyImmediate(spawner);
    }
}
#endif
