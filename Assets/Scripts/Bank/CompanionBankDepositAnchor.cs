using System.Collections.Generic;
using UnityEngine;

namespace BankSystem
{
    /// <summary>
    /// Editor tag component that marks a world object as a valid companion bank deposit anchor.
    /// Companions standing within the configured radius will be allowed to stream items into the bank.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Bank/Companion Bank Deposit Anchor")]
    public sealed class CompanionBankDepositAnchor : MonoBehaviour
    {
        private const float DefaultRadius = 10f;

        /// <summary>
        /// Shared registry of active anchors used when resolving bank proximity checks.
        /// </summary>
        private static readonly List<CompanionBankDepositAnchor> ActiveAnchors = new List<CompanionBankDepositAnchor>();

        [SerializeField]
        [Tooltip("Radius in tiles (1 tile == 1 Unity unit) where companion bank deposits are allowed."), Min(0f)]
        private float depositRadius = DefaultRadius;

        private void Reset()
        {
            depositRadius = DefaultRadius;
        }

        private void OnValidate()
        {
            depositRadius = Mathf.Max(0f, depositRadius);
        }

        private void OnEnable()
        {
            if (!ActiveAnchors.Contains(this))
                ActiveAnchors.Add(this);
        }

        private void OnDisable()
        {
            ActiveAnchors.Remove(this);
        }

        /// <summary>
        /// Checks whether the supplied position is within range of any registered companion bank anchor.
        /// </summary>
        /// <param name="playerPosition">World position that should be evaluated.</param>
        /// <returns>True if an anchor is close enough to accept deposits; otherwise false.</returns>
        public static bool IsPlayerWithinDepositRange(Vector3 playerPosition)
        {
            for (int i = ActiveAnchors.Count - 1; i >= 0; i--)
            {
                var anchor = ActiveAnchors[i];
                if (anchor == null || !anchor.isActiveAndEnabled)
                {
                    ActiveAnchors.RemoveAt(i);
                    continue;
                }

                if (anchor.IsWithinRange(playerPosition))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Evaluates whether the provided position sits inside this anchor's configured radius.
        /// </summary>
        private bool IsWithinRange(Vector3 position)
        {
            float sqrRadius = depositRadius * depositRadius;
            return (transform.position - position).sqrMagnitude <= sqrRadius;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.9f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, depositRadius);
            Gizmos.color = new Color(0.8f, 0.9f, 1f, 0.15f);
            Gizmos.DrawSphere(transform.position, depositRadius);
        }
#endif
    }
}
