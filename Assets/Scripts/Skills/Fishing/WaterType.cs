using System;

namespace Skills.Fishing
{
    [Flags]
    public enum WaterType
    {
        None = 0,
        Freshwater = 1,
        Saltwater = 2,
        Brackish = 4,
        Any = ~0
    }
}
