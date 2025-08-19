using System;
using Inventory;
using UnityEngine;

namespace MyGame.Drops
{
    /// <summary>
    /// Represents either a fixed quantity or a range of quantities.
    /// </summary>
    [Serializable]
    public class DropQuantity
    {
        /// <summary>Whether to use a range (<see cref="min"/> to <see cref="max"/>).</summary>
        public bool useRange = false;

        /// <summary>Minimum quantity when using a range or the fixed amount.</summary>
        public int min = 1;

        /// <summary>Maximum quantity when using a range.</summary>
        public int max = 1;

        /// <summary>
        /// Resolves a quantity using the supplied random generator.
        /// </summary>
        /// <param name="random">Optional random source.</param>
        /// <returns>An integer quantity.</returns>
        public int Get(System.Random random = null)
        {
            int a = min;
            int b = useRange ? max : min;
            if (a >= b)
            {
                return a;
            }

            if (random != null)
            {
                return random.Next(a, b + 1);
            }

            return UnityEngine.Random.Range(a, b + 1);
        }
    }

    /// <summary>
    /// Weighted entry in a drop table.
    /// </summary>
    [Serializable]
    public class WeightedDropEntry
    {
        /// <summary>Item to drop.</summary>
        public ItemData item;

        /// <summary>Quantity definition.</summary>
        public DropQuantity qty = new DropQuantity();

        /// <summary>Selection weight.</summary>
        public int weight = 1;

        /// <summary>Whether this entry's weight is affected by luck.</summary>
        public bool affectedByLuck = true;

        /// <summary>Whether this entry always drops in addition to weighted rolls.</summary>
        public bool alwaysDrop = false;
    }

    /// <summary>
    /// Unique drop with an independent 1/N chance.
    /// </summary>
    [Serializable]
    public class UniqueDropEntry
    {
        public ItemData item;
        public DropQuantity qty = new DropQuantity();
        public int denominator = 128;
        public bool affectedByLuck = true;
    }

    /// <summary>
    /// Independent tertiary drop performed after main rolls.
    /// </summary>
    [Serializable]
    public class TertiaryDropEntry
    {
        public ItemData item;
        public DropQuantity qty = new DropQuantity();
        public int denominator = 128;
        public bool affectedByLuck = true;
    }

    /// <summary>
    /// Marker class used to indicate a Rare Drop Table roll.
    /// </summary>
    [Serializable]
    public class RareDropPlaceholder : WeightedDropEntry
    {
        public RareDropPlaceholder()
        {
            item = null;
        }
    }
}
