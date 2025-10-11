using System.Reflection;
using Inventory;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Verifies inventory removal behaviour when insufficient quantities are available.
/// </summary>
public sealed class InventoryRemovalTests
{
    /// <summary>
    /// Configures an inventory instance with a single stack of the supplied item.
    /// The GameObject remains inactive so editor-only dependencies do not run during tests.
    /// </summary>
    private static Inventory.Inventory CreateInventoryWithStack(ItemData item, int count)
    {
        var inventoryObject = new GameObject("InventoryTest");
        inventoryObject.SetActive(false);
        var inventory = inventoryObject.AddComponent<Inventory.Inventory>();

        var itemsField = typeof(Inventory.Inventory).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(itemsField, "Inventory.items field should exist for test configuration.");

        var slots = new Inventory.InventoryEntry[inventory.size];
        slots[0] = new Inventory.InventoryEntry { item = item, count = count };
        itemsField.SetValue(inventory, slots);

        return inventory;
    }

    [Test]
    public void RemoveItemFailsWhenInsufficientQuantity()
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.stackable = true;

        Inventory.Inventory inventory = null;
        try
        {
            inventory = CreateInventoryWithStack(item, 2);
            int initialCount = inventory.GetItemCount(item);

            bool removed = inventory.RemoveItem(item, 3);

            Assert.IsFalse(removed, "Inventory should not remove items when insufficient quantity exists.");
            Assert.AreEqual(initialCount, inventory.GetItemCount(item), "Item count should remain unchanged after failed removal.");
        }
        finally
        {
            if (inventory != null)
                Object.DestroyImmediate(inventory.gameObject);
            ScriptableObject.DestroyImmediate(item);
        }
    }
}
