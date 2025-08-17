using UnityEngine;

namespace BankSystem
{
    /// <summary>
    /// Attach to a bank object. Clicking it opens the bank if the player is
    /// within a specified distance.
    /// </summary>
    public class BankOpener : MonoBehaviour
    {
        public float openDistance = 1.5f;

        private void OnMouseDown()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return;
            if (Vector2.Distance(player.transform.position, transform.position) > openDistance)
                return;
            BankUI.Instance?.Open();
        }
    }
}
