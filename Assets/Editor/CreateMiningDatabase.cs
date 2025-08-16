#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Skills.Mining;

public static class CreateMiningDatabase
{
    [MenuItem("Tools/Create Mining Database")]
    public static void Create()
    {
        string basePath = "Assets/MiningDatabase";
        if (!Directory.Exists(basePath))
            Directory.CreateDirectory(basePath);

        // XP Table
        var xpTable = ScriptableObject.CreateInstance<XpTable>();
        xpTable.name = "MiningXpTable";
        var xp = XpTable.GenerateXpTable();
        typeof(XpTable).GetField("levelXp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(xpTable, xp);
        AssetDatabase.CreateAsset(xpTable, Path.Combine(basePath, "MiningXpTable.asset"));

        // Ores
        var ores = new List<OreDefinition>();
        ores.Add(CreateOre(basePath, "ore_copper", "Copper ore", 18, 1));
        ores.Add(CreateOre(basePath, "ore_tin", "Tin ore", 18, 1));
        ores.Add(CreateOre(basePath, "ore_iron", "Iron ore", 35, 15));
        ores.Add(CreateOre(basePath, "ore_coal", "Coal", 50, 30));
        ores.Add(CreateOre(basePath, "ore_mithril", "Mithril ore", 80, 55));
        ores.Add(CreateOre(basePath, "ore_adamantite", "Adamantite ore", 95, 70));
        ores.Add(CreateOre(basePath, "ore_runite", "Runite ore", 125, 85));

        // Pickaxes
        var pickaxes = new List<PickaxeDefinition>();
        pickaxes.Add(CreatePickaxe(basePath, "pickaxe_bronze", "Bronze pickaxe", 1, 1, 0f, 5));
        pickaxes.Add(CreatePickaxe(basePath, "pickaxe_iron", "Iron pickaxe", 2, 1, 0.01f, 5));
        pickaxes.Add(CreatePickaxe(basePath, "pickaxe_steel", "Steel pickaxe", 3, 6, 0.02f, 4));
        pickaxes.Add(CreatePickaxe(basePath, "pickaxe_mithril", "Mithril pickaxe", 4, 21, 0.03f, 4));
        pickaxes.Add(CreatePickaxe(basePath, "pickaxe_adamant", "Adamant pickaxe", 5, 31, 0.04f, 3));
        pickaxes.Add(CreatePickaxe(basePath, "pickaxe_rune", "Rune pickaxe", 6, 41, 0.05f, 3));

        // Rocks
        var rocks = new List<RockDefinition>();
        rocks.Add(CreateRock(basePath, "rock_copper", ores[0], 0.33f, 2f, 4f, 1, 0));
        rocks.Add(CreateRock(basePath, "rock_tin", ores[1], 0.33f, 2f, 4f, 1, 0));
        rocks.Add(CreateRock(basePath, "rock_iron", ores[2], 0.5f, 4f, 7f, 1, 0));
        rocks.Add(CreateRock(basePath, "rock_coal", ores[3], 0.4f, 15f, 30f, 2, 0));
        rocks.Add(CreateRock(basePath, "rock_mithril", ores[4], 0.5f, 60f, 120f, 3, 0));
        rocks.Add(CreateRock(basePath, "rock_adamantite", ores[5], 0.5f, 90f, 180f, 4, 0));
        rocks.Add(CreateRock(basePath, "rock_runite", ores[6], 1f, 900f, 1200f, 5, 1));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        CreateDemoScene(basePath, rocks);
    }

    private static OreDefinition CreateOre(string basePath, string id, string name, int xp, int level)
    {
        var ore = ScriptableObject.CreateInstance<OreDefinition>();
        ore.name = id;
        ore.GetType().GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(ore, id);
        ore.GetType().GetField("displayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(ore, name);
        ore.GetType().GetField("xpPerOre", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(ore, xp);
        ore.GetType().GetField("levelRequirement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(ore, level);
        AssetDatabase.CreateAsset(ore, Path.Combine(basePath, id + ".asset"));
        return ore;
    }

    private static PickaxeDefinition CreatePickaxe(string basePath, string id, string name, int tier, int level, float bonus, int speed)
    {
        var pick = ScriptableObject.CreateInstance<PickaxeDefinition>();
        pick.name = id;
        typeof(PickaxeDefinition).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(pick, id);
        typeof(PickaxeDefinition).GetField("displayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(pick, name);
        typeof(PickaxeDefinition).GetField("tier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(pick, tier);
        typeof(PickaxeDefinition).GetField("levelRequirement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(pick, level);
        typeof(PickaxeDefinition).GetField("miningRollBonus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(pick, bonus);
        typeof(PickaxeDefinition).GetField("swingSpeedTicks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(pick, speed);
        AssetDatabase.CreateAsset(pick, Path.Combine(basePath, id + ".asset"));
        return pick;
    }

    private static RockDefinition CreateRock(string basePath, string id, OreDefinition ore, float depleteChance, float respawnMin, float respawnMax, int tierReq, int depleteAfter)
    {
        var rock = ScriptableObject.CreateInstance<RockDefinition>();
        rock.name = id;
        typeof(RockDefinition).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(rock, id);
        typeof(RockDefinition).GetField("ore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(rock, ore);
        typeof(RockDefinition).GetField("depletionRoll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(rock, depleteChance);
        typeof(RockDefinition).GetField("respawnTimeSecondsMin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(rock, respawnMin);
        typeof(RockDefinition).GetField("respawnTimeSecondsMax", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(rock, respawnMax);
        typeof(RockDefinition).GetField("requiresToolTier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(rock, tierReq);
        typeof(RockDefinition).GetField("depleteAfterNOres", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(rock, depleteAfter);
        AssetDatabase.CreateAsset(rock, Path.Combine(basePath, id + ".asset"));
        return rock;
    }

    private static void CreateDemoScene(string basePath, List<RockDefinition> rocks)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        for (int i = 0; i < Mathf.Min(rocks.Count, 3); i++)
        {
            var go = new GameObject(rocks[i].name);
            var mr = go.AddComponent<MineableRock>();
            mr.rockDef = rocks[i];
            go.transform.position = new Vector3(i * 2f, 0f, 0f);
        }
        string scenePath = Path.Combine(basePath, "MiningDemo.unity");
        EditorSceneManager.SaveScene(scene, scenePath);
    }
}
#endif
