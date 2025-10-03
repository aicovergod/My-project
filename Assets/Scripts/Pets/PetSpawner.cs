using UnityEngine;
using UnityEngine.AI;
using Util;
using Skills.Cooking;

namespace Pets
{
    /// <summary>
    /// Builds a runtime pet instance based on a <see cref="PetDefinition"/>.
    /// </summary>
    public static class PetSpawner
    {
        /// <summary>
        /// Create a pet game object at the given position.
        /// </summary>
        public static GameObject Spawn(PetDefinition def, Vector3 position, Transform player = null)
        {
            if (def == null)
            {
                Debug.LogWarning("PetSpawner.Spawn called with null definition.");
                return null;
            }

            var go = new GameObject($"Pet_{def.id}");
            go.transform.position = position;

            // Ensure the spawned pet uses the dedicated physics layer so collision filters,
            // targeting, and raycasts that operate on layer masks treat the familiar correctly.
            // The live project names the layer "Pets" (layer index 9) but we honour a couple of
            // legacy names in case older data still relies on a different capitalisation.
            int petLayer = ResolveLayerIndex("Pets", "Pet", "PET");
            if (petLayer >= 0)
                go.layer = petLayer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "Characters";
            if (player != null)
            {
                var playerSr = player.GetComponent<SpriteRenderer>();
                if (playerSr != null)
                    sr.sortingLayerID = playerSr.sortingLayerID;
            }
            sr.sprite = def.sprite;
            if (sr.sprite == null)
            {
                if (def.idleDown != null && def.idleDown.Length > 0) sr.sprite = def.idleDown[0];
                else if (def.walkDown != null && def.walkDown.Length > 0) sr.sprite = def.walkDown[0];
                else if (def.idleDownRight != null && def.idleDownRight.Length > 0) sr.sprite = def.idleDownRight[0];
                else if (def.walkDownRight != null && def.walkDownRight.Length > 0) sr.sprite = def.walkDownRight[0];
                else if (def.idleRight != null && def.idleRight.Length > 0) sr.sprite = def.idleRight[0];
                else if (def.walkRight != null && def.walkRight.Length > 0) sr.sprite = def.walkRight[0];
                else if (def.idleUpRight != null && def.idleUpRight.Length > 0) sr.sprite = def.idleUpRight[0];
                else if (def.walkUpRight != null && def.walkUpRight.Length > 0) sr.sprite = def.walkUpRight[0];
                else if (def.idleUp != null && def.idleUp.Length > 0) sr.sprite = def.idleUp[0];
                else if (def.walkUp != null && def.walkUp.Length > 0) sr.sprite = def.walkUp[0];
                else if (def.idleUpLeft != null && def.idleUpLeft.Length > 0) sr.sprite = def.idleUpLeft[0];
                else if (def.walkUpLeft != null && def.walkUpLeft.Length > 0) sr.sprite = def.walkUpLeft[0];
                else if (def.idleLeft != null && def.idleLeft.Length > 0) sr.sprite = def.idleLeft[0];
                else if (def.walkLeft != null && def.walkLeft.Length > 0) sr.sprite = def.walkLeft[0];
                else if (def.idleDownLeft != null && def.idleDownLeft.Length > 0) sr.sprite = def.idleDownLeft[0];
                else if (def.walkDownLeft != null && def.walkDownLeft.Length > 0) sr.sprite = def.walkDownLeft[0];
            }
            if (sr.sprite != null && sr.sprite.texture != null)
                sr.sprite.texture.filterMode = FilterMode.Point;

            // Scale based on desired pixels per unit
            if (def.pixelsPerUnit > 0f)
            {
                float spritePPU = sr.sprite != null ? sr.sprite.pixelsPerUnit : 64f;
                float scale = spritePPU / def.pixelsPerUnit;
                go.transform.localScale = new Vector3(scale, scale, 1f);
            }

            bool hasFrameSprites =
                (def.idleDown != null && def.idleDown.Length > 0) ||
                (def.walkDown != null && def.walkDown.Length > 0) ||
                (def.idleDownRight != null && def.idleDownRight.Length > 0) ||
                (def.walkDownRight != null && def.walkDownRight.Length > 0) ||
                (def.idleRight != null && def.idleRight.Length > 0) ||
                (def.walkRight != null && def.walkRight.Length > 0) ||
                (def.idleUpRight != null && def.idleUpRight.Length > 0) ||
                (def.walkUpRight != null && def.walkUpRight.Length > 0) ||
                (def.idleUp != null && def.idleUp.Length > 0) ||
                (def.walkUp != null && def.walkUp.Length > 0) ||
                (def.idleUpLeft != null && def.idleUpLeft.Length > 0) ||
                (def.walkUpLeft != null && def.walkUpLeft.Length > 0) ||
                (def.idleLeft != null && def.idleLeft.Length > 0) ||
                (def.walkLeft != null && def.walkLeft.Length > 0) ||
                (def.idleDownLeft != null && def.idleDownLeft.Length > 0) ||
                (def.walkDownLeft != null && def.walkDownLeft.Length > 0) ||
                (def.hitDown != null && def.hitDown.Length > 0) ||
                (def.hitDownRight != null && def.hitDownRight.Length > 0) ||
                (def.hitRight != null && def.hitRight.Length > 0) ||
                (def.hitUpRight != null && def.hitUpRight.Length > 0) ||
                (def.hitUp != null && def.hitUp.Length > 0) ||
                (def.hitUpLeft != null && def.hitUpLeft.Length > 0) ||
                (def.hitLeft != null && def.hitLeft.Length > 0) ||
                (def.hitDownLeft != null && def.hitDownLeft.Length > 0);

            if (hasFrameSprites && (def.animationClips == null || def.animationClips.Length == 0))
            {
                var spriteAnim = go.AddComponent<PetSpriteAnimator>();
                spriteAnim.spriteRenderer = sr;
                spriteAnim.idleDown = def.idleDown;
                spriteAnim.idleDownRight = def.idleDownRight;
                spriteAnim.idleRight = def.idleRight;
                spriteAnim.idleUpRight = def.idleUpRight;
                spriteAnim.idleUp = def.idleUp;
                spriteAnim.idleUpLeft = def.idleUpLeft;
                spriteAnim.idleLeft = def.idleLeft;
                spriteAnim.idleDownLeft = def.idleDownLeft;
                spriteAnim.walkDown = def.walkDown;
                spriteAnim.walkDownRight = def.walkDownRight;
                spriteAnim.walkRight = def.walkRight;
                spriteAnim.walkUpRight = def.walkUpRight;
                spriteAnim.walkUp = def.walkUp;
                spriteAnim.walkUpLeft = def.walkUpLeft;
                spriteAnim.walkLeft = def.walkLeft;
                spriteAnim.walkDownLeft = def.walkDownLeft;
                spriteAnim.hitDown = def.hitDown;
                spriteAnim.hitDownRight = def.hitDownRight;
                spriteAnim.hitRight = def.hitRight;
                spriteAnim.hitUpRight = def.hitUpRight;
                spriteAnim.hitUp = def.hitUp;
                spriteAnim.hitUpLeft = def.hitUpLeft;
                spriteAnim.hitLeft = def.hitLeft;
                spriteAnim.hitDownLeft = def.hitDownLeft;
                spriteAnim.useFlipXForLeft = def.useRightSpritesForLeft;
                spriteAnim.useFlipXForRight = def.useLeftSpritesForRight;
                spriteAnim.useFlipXForDownLeft = def.useRightSpritesForDownLeft;
                spriteAnim.useFlipXForDownRight = def.useLeftSpritesForDownRight;
                spriteAnim.useFlipXForUpLeft = def.useRightSpritesForUpLeft;
                spriteAnim.useFlipXForUpRight = def.useLeftSpritesForUpRight;
                if (def.baseAnimationFPS > 0f)
                    spriteAnim.animationFPS = def.baseAnimationFPS;
                spriteAnim.idleAnimationFPS = def.idleAnimationFPS;
                spriteAnim.walkAnimationFPS = def.walkAnimationFPS;
                spriteAnim.hitAnimationFPS = def.hitAnimationFPS;
            }

            if (def.animationClips != null && def.animationClips.Length > 0)
            {
                var animator = go.AddComponent<Animator>();
#if UNITY_EDITOR
                var controller = new UnityEditor.Animations.AnimatorController();
                controller.AddLayer("Base");
                var sm = controller.layers[0].stateMachine;
                foreach (var clip in def.animationClips)
                {
                    if (clip == null) continue;
                    var state = sm.AddState(clip.name);
                    state.motion = clip;
                }
                animator.runtimeAnimatorController = controller;
#else
                var anim = go.AddComponent<Animation>();
                foreach (var clip in def.animationClips)
                {
                    if (clip == null) continue;
                    anim.AddClip(clip, clip.name);
                }
                if (def.animationClips.Length > 0)
                {
                    anim.clip = def.animationClips[0];
                    anim.Play();
                }
#endif
            }

            var rb = go.AddComponent<Rigidbody2D>();
            rb.isKinematic = true;
            rb.gravityScale = 0f;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.3f;

            var follower = go.AddComponent<PetFollower>();
            if (player != null)
                follower.SetPlayer(player);

            var exp = go.AddComponent<PetExperience>();
            exp.definition = def;

            PetStorage storage = null;
            if (def.hasInventory)
            {
                storage = go.AddComponent<PetStorage>();
                storage.Initialize(def);
            }

            var clickable = go.AddComponent<PetClickable>();
            clickable.Init(def, storage);

            if (def.canFight)
            {
                var agent = go.AddComponent<NavMeshAgent>();
                agent.updateRotation = false;
                agent.updateUpAxis = false;
                var combat = go.AddComponent<PetCombatController>();
                combat.definition = def;
            }

            if (def.id == "Mr Frying Pan")
            {
                // Add a collider that can receive mouse events for cooking interaction.
                var interactCol = go.AddComponent<BoxCollider2D>();
                interactCol.isTrigger = true;

                // Optionally place on a layer that does not collide with the environment
                // so pathfinding remains unaffected.
                int interactionLayer = LayerMask.NameToLayer("PetInteraction");
                if (interactionLayer >= 0)
                {
                    interactCol.gameObject.layer = interactionLayer;
                    int playerLayer = LayerMask.NameToLayer("Player");
                    if (playerLayer >= 0)
                        Physics2D.IgnoreLayerCollision(interactionLayer, playerLayer);
                }

                go.AddComponent<CookingObject>();
            }

            var floatingTextController = go.GetComponent<PetFloatingTextController>();
            if (floatingTextController == null)
                floatingTextController = go.AddComponent<PetFloatingTextController>();
            floatingTextController.RefreshAnchorPosition();

            return go;
        }

        /// <summary>
        ///     Attempts to resolve the first valid physics layer index from the supplied list of
        ///     potential names. Returns -1 when none of the names map to a configured layer.
        /// </summary>
        private static int ResolveLayerIndex(params string[] layerNames)
        {
            if (layerNames == null || layerNames.Length == 0)
                return -1;

            for (int i = 0; i < layerNames.Length; i++)
            {
                string layerName = layerNames[i];
                if (string.IsNullOrEmpty(layerName))
                    continue;

                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0)
                    return layer;
            }

            return -1;
        }
    }
}
