#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Status.Poison;

/// <summary>
/// Editor tools for testing poison application and curing.
/// </summary>
public static class PoisonDebugMenu
{
    [MenuItem("Tools/Status/Apply Poison (p)")]
    private static void ApplyPoisonP()
    {
        Apply("poison_p");
    }

    [MenuItem("Tools/Status/Apply Poison (p++)")]
    private static void ApplyPoisonPP()
    {
        Apply("poison_pp");
    }

    [MenuItem("Tools/Status/Cure (6m)")]
    private static void Cure6m()
    {
        Cure(360f);
    }

    [MenuItem("Tools/Status/Cure (12m)")]
    private static void Cure12m()
    {
        Cure(720f);
    }

    private static void Apply(string id)
    {
        var go = Selection.activeGameObject;
        if (go == null)
            return;
        var controller = go.GetComponent<PoisonController>();
        if (controller == null)
            controller = go.AddComponent<PoisonController>();
        var cfg = Resources.Load<PoisonConfig>($"Status/Poison/{id}");
        if (cfg != null)
            controller.ApplyPoison(cfg);
        Debug.Log($"Applied {id} at {Time.time}");
    }

    private static void Cure(float immunity)
    {
        var go = Selection.activeGameObject;
        if (go == null)
            return;
        var controller = go.GetComponent<PoisonController>();
        controller?.CurePoison(immunity);
        Debug.Log($"Cured poison at {Time.time}");
    }
}
#endif
