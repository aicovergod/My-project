using UnityEngine;

/// <summary>
/// Simple data container describing an item that can be stored in the
/// player's inventory.  It is implemented as a ScriptableObject so items can
/// be created easily via the Unity editor and referenced by pickup objects.
/// </summary>
[CreateAssetMenu(menuName = "Inventory/ItemData")]
public class ItemData : ScriptableObject
{
    [Header("Identification")]
    public string id;

    [Header("Display")]
    public string itemName;
    public Sprite icon;

    [Header("Description")]
    [TextArea]
    public string description;

    [Header("Stacking")]
    [Tooltip("If true, multiple items can occupy a single inventory slot.")]
    public bool stackable;

    [Tooltip("Maximum number of items per stack when stackable.")]
    public int maxStack = 1;

    [Tooltip("If true, stacks of this item can be split in the inventory.")]
    public bool splittable = true;
}