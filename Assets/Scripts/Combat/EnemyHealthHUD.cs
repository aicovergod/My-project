using UnityEngine;
using UnityEngine.UI;
using Player;

namespace Combat
{
    /// <summary>
    /// Displays the engaged enemy's health above the NPC during combat.
    /// </summary>
    public class EnemyHealthHUD : MonoBehaviour
    {
        private GameObject barRoot;
        private Image fillImage;
        private Text text;
        private Enemy currentEnemy;
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            var go = new GameObject("EnemyHealthHUD");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<EnemyHealthHUD>();
        }

        private void Awake()
        {
            var manager = CombatManager.Instance;
            if (manager != null)
            {
                manager.OnCombatStarted += HandleStart;
                manager.OnCombatEnded += HandleStop;
            }
            CreateBar();
        }

        private void CreateBar()
        {
            barRoot = new GameObject("EnemyHealthBar");
            barRoot.transform.SetParent(transform);

            var canvas = barRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            barRoot.AddComponent<CanvasScaler>();
            barRoot.AddComponent<GraphicRaycaster>();
            barRoot.transform.localScale = Vector3.one * 0.01f;

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(barRoot.transform, false);
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = Color.red;
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            bgImage.sprite = sprite;
            var bgRect = bgImage.rectTransform;
            bgRect.sizeDelta = new Vector2(150f, 25f);

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(bgGO.transform, false);
            fillImage = fillGO.AddComponent<Image>();
            fillImage.color = Color.green;
            fillImage.sprite = sprite;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 1f;
            var fillRect = fillImage.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(bgGO.transform, false);
            text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 11;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            barRoot.SetActive(false);
        }

        private void HandleStart(PlayerCombat player, Enemy enemy)
        {
            currentEnemy = enemy;
            UpdateBar();
            if (barRoot != null)
                barRoot.SetActive(true);
        }

        private void HandleStop(PlayerCombat player, Enemy enemy)
        {
            currentEnemy = null;
            if (barRoot != null)
            {
                barRoot.SetActive(false);
            }
        }

        private void Update()
        {
            if (currentEnemy != null && barRoot != null && barRoot.activeSelf)
            {
                barRoot.transform.position = currentEnemy.transform.position + offset;
                UpdateBar();
            }
        }

        private void UpdateBar()
        {
            if (currentEnemy == null || fillImage == null || text == null)
                return;
            int current = currentEnemy.CurrentHitpoints;
            int max = currentEnemy.MaxHitpoints;
            fillImage.fillAmount = max > 0 ? (float)current / max : 0f;
            text.text = $"{current}/{max}";
        }

        private void OnDestroy()
        {
            var manager = CombatManager.Instance;
            if (manager != null)
            {
                manager.OnCombatStarted -= HandleStart;
                manager.OnCombatEnded -= HandleStop;
            }
        }
    }
}

