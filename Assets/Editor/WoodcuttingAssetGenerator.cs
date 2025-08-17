#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Skills.Woodcutting;

public static class WoodcuttingAssetGenerator
{
    [MenuItem("Tools/Create Woodcutting Assets")]
    public static void Create()
    {
        string basePath = "Assets/WoodcuttingDatabase";
        if (!Directory.Exists(basePath))
            Directory.CreateDirectory(basePath);

        // Axes
        CreateAxe(basePath, "axe_bronze", "Bronze axe", 1, 1f, 1);
        CreateAxe(basePath, "axe_iron", "Iron axe", 1, 1.1f, 2);
        CreateAxe(basePath, "axe_steel", "Steel axe", 6, 1.2f, 3);
        CreateAxe(basePath, "axe_black", "Black axe", 11, 1.25f, 4);
        CreateAxe(basePath, "axe_mithril", "Mithril axe", 21, 1.3f, 5);
        CreateAxe(basePath, "axe_adamant", "Adamant axe", 31, 1.35f, 6);
        CreateAxe(basePath, "axe_rune", "Rune axe", 41, 1.4f, 7);
        CreateAxe(basePath, "axe_dragon", "Dragon axe", 61, 1.5f, 8);

        // Trees
        CreateTree(basePath, "tree_normal", "Tree", 1, 25, "log_normal", true, 8, 10, 4);
        CreateTree(basePath, "tree_oak", "Oak", 15, 38, "log_oak", false, 8, 27, 4);
        CreateTree(basePath, "tree_willow", "Willow", 30, 68, "log_willow", false, 8, 30, 4);
        CreateTree(basePath, "tree_maple", "Maple", 45, 100, "log_maple", false, 8, 60, 4);
        CreateTree(basePath, "tree_yew", "Yew", 60, 175, "log_yew", false, 8, 114, 4);
        CreateTree(basePath, "tree_magic", "Magic", 75, 250, "log_magic", false, 8, 234, 4);
        CreateTree(basePath, "tree_redwood", "Redwood", 90, 380, "log_redwood", false, 8, 264, 4);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static AxeDefinition CreateAxe(string basePath, string id, string name, int level, float speed, int power)
    {
        var axe = ScriptableObject.CreateInstance<AxeDefinition>();
        axe.name = id;
        typeof(AxeDefinition).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(axe, id);
        typeof(AxeDefinition).GetField("displayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(axe, name);
        typeof(AxeDefinition).GetField("requiredWoodcuttingLevel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(axe, level);
        typeof(AxeDefinition).GetField("swingSpeedMultiplier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(axe, speed);
        typeof(AxeDefinition).GetField("power", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(axe, power);
        AssetDatabase.CreateAsset(axe, Path.Combine(basePath, id + ".asset"));
        return axe;
    }

    private static TreeDefinition CreateTree(string basePath, string id, string name, int level, int xp, string logId, bool single, int depleteRollInv, int respawn, int interval)
    {
        var tree = ScriptableObject.CreateInstance<TreeDefinition>();
        tree.name = id;
        typeof(TreeDefinition).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, id);
        typeof(TreeDefinition).GetField("displayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, name);
        typeof(TreeDefinition).GetField("requiredWoodcuttingLevel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, level);
        typeof(TreeDefinition).GetField("xpPerLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, xp);
        typeof(TreeDefinition).GetField("logItemId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, logId);
        typeof(TreeDefinition).GetField("depletesAfterOneLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, single);
        typeof(TreeDefinition).GetField("depleteRollInverse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, depleteRollInv);
        typeof(TreeDefinition).GetField("respawnSeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, respawn);
        typeof(TreeDefinition).GetField("chopIntervalTicks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, interval);
        AssetDatabase.CreateAsset(tree, Path.Combine(basePath, id + ".asset"));
        return tree;
    }
}
#endif
