using UnityEngine;
using Status.Poison;

namespace Items.Consumables
{
    /// <summary>
    /// Item that cures poison and grants temporary immunity.
    /// </summary>
    [CreateAssetMenu(menuName = "Items/Consumables/Antipoison")]
    public class AntipoisonItem : ScriptableObject
    {
        [Tooltip("Display name for UI.")]
        public string displayName;

        [Tooltip("Duration of poison immunity in seconds.")]
        public float immunitySeconds;

        [Tooltip("Icon used to represent the item.")]
        public Sprite icon;

        /// <summary>
        /// Use the item on the given user to cure poison.
        /// </summary>
        public void Use(GameObject user)
        {
            if (user == null)
                return;
            var controller = user.GetComponentInParent<PoisonController>();
            controller?.CurePoison(immunitySeconds);
        }
    }
}
