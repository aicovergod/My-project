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
        public static GameObject Spawn(PetDefinition def, Vector3 position)
        {
            if (def == null)
            {
                Debug.LogWarning("PetSpawner.Spawn called with null definition.");
                return null;
            }

            PhysicsLayerUtility.Ensure();
            int layer = LayerMask.NameToLayer("Pets");

            var go = new GameObject($"Pet_{def.id}");
            go.layer = layer;
            go.transform.position = position;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = def.sprite;
            sr.sortingLayerName = "Characters";
            if (def.sprite != null && def.sprite.texture != null)
                def.sprite.texture.filterMode = FilterMode.Point;

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

            go.AddComponent<PetFollower>();
            var clickable = go.AddComponent<PetClickable>();
            clickable.Init(def);

            return go;
        }
    }
}