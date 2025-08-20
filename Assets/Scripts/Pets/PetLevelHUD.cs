using UnityEngine;
using UnityEngine.UI;

namespace Pets
{
    /// <summary>
    /// Displays the pet's current level above its head.
    /// </summary>
    [RequireComponent(typeof(PetExperience))]
    public class PetLevelHUD : MonoBehaviour
    {
        private PetExperience experience;
        private Canvas canvas;
        private Text text;

        private void Awake()
        {
            experience = GetComponent<PetExperience>();
            experience.OnLevelChanged += HandleLevelChanged;
            CreateHud();
            HandleLevelChanged(experience.Level);
        }

        private void CreateHud()
        {
            var go = new GameObject("PetLevelHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.transform.SetParent(transform, false);
            canvas.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            canvas.transform.localRotation = Quaternion.identity;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 0.3f);

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(canvas.transform, false);
            text = textGO.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 14;
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void HandleLevelChanged(int lvl)
        {
            if (text != null)
                text.text = $"Lv {lvl}";
        }

        private void OnDestroy()
        {
            if (experience != null)
                experience.OnLevelChanged -= HandleLevelChanged;
        }
    }
}

