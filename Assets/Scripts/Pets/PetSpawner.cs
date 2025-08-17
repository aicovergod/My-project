using UnityEngine;

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

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "Characters";
            sr.sprite = def.sprite;
            if (sr.sprite == null)
            {
                if (def.idleDown != null && def.idleDown.Length > 0) sr.sprite = def.idleDown[0];
                else if (def.walkDown != null && def.walkDown.Length > 0) sr.sprite = def.walkDown[0];
                else if (def.idleRight != null && def.idleRight.Length > 0) sr.sprite = def.idleRight[0];
                else if (def.walkRight != null && def.walkRight.Length > 0) sr.sprite = def.walkRight[0];
                else if (def.idleUp != null && def.idleUp.Length > 0) sr.sprite = def.idleUp[0];
                else if (def.walkUp != null && def.walkUp.Length > 0) sr.sprite = def.walkUp[0];
                else if (def.idleLeft != null && def.idleLeft.Length > 0) sr.sprite = def.idleLeft[0];
                else if (def.walkLeft != null && def.walkLeft.Length > 0) sr.sprite = def.walkLeft[0];
            }
            if (sr.sprite != null && sr.sprite.texture != null)
                sr.sprite.texture.filterMode = FilterMode.Point;

            bool hasFrameSprites =
                (def.idleUp != null && def.idleUp.Length > 0) ||
                (def.walkUp != null && def.walkUp.Length > 0) ||
                (def.idleDown != null && def.idleDown.Length > 0) ||
                (def.walkDown != null && def.walkDown.Length > 0) ||
                (def.idleLeft != null && def.idleLeft.Length > 0) ||
                (def.walkLeft != null && def.walkLeft.Length > 0) ||
                (def.idleRight != null && def.idleRight.Length > 0) ||
                (def.walkRight != null && def.walkRight.Length > 0);

            if (hasFrameSprites && (def.animationClips == null || def.animationClips.Length == 0))
            {
                var spriteAnim = go.AddComponent<PetSpriteAnimator>();
                spriteAnim.spriteRenderer = sr;
                spriteAnim.idleUp = def.idleUp;
                spriteAnim.walkUp = def.walkUp;
                spriteAnim.idleDown = def.idleDown;
                spriteAnim.walkDown = def.walkDown;
                spriteAnim.idleLeft = def.idleLeft;
                spriteAnim.walkLeft = def.walkLeft;
                spriteAnim.idleRight = def.idleRight;
                spriteAnim.walkRight = def.walkRight;
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
            var clickable = go.AddComponent<PetClickable>();
            clickable.Init(def);

            return go;
        }
    }
}
