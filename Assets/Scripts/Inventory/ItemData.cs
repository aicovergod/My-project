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
    public Sprite icon;

    [Header("Description")]
    [TextArea]
    public string description;
}