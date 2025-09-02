using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using Player;

namespace Pets
{
    /// <summary>
    /// Displays the pet's level in a golden bar under the player's health bar.
    /// </summary>
    public class PetLevelBarHUD : MonoBehaviour, IPointerClickHandler
    {
        private static PetLevelBarHUD instance;

        private PetExperience experience;
        private Text text;
        private Coroutine xpRoutine;

        /// <summary>
        /// Create the pet level bar under the existing health bar.
        /// If a bar already exists it will be replaced.
        /// </summary>
        public static void CreateForPet(PetExperience exp)
        {
            if (exp == null)
                return;

            if (instance != null)
                Destroy(instance.gameObject);

            var healthHud = Object.FindObjectOfType<HealthHUD>();
            if (healthHud == null)
                return;

            var healthRect = healthHud.GetComponent<RectTransform>();
            var parent = healthRect.parent as RectTransform;
            var go = new GameObject("PetLevelHUD", typeof(RectTransform), typeof(PetLevelBarHUD));
            instance = go.GetComponent<PetLevelBarHUD>();
            instance.experience = exp;
            go.transform.SetParent(parent, false);

            var sprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));

            const float margin = 2f;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = healthRect.anchorMin;
            rect.anchorMax = healthRect.anchorMax;
            rect.pivot = healthRect.pivot;
            rect.sizeDelta = healthRect.sizeDelta;
            rect.anchoredPosition = healthRect.anchoredPosition + new Vector2(0f, -(healthRect.sizeDelta.y + margin));

            var bgGO = new GameObject("Background", typeof(Image));
            bgGO.transform.SetParent(go.transform, false);
            var bgImg = bgGO.GetComponent<Image>();
            bgImg.color = Color.black;
            bgImg.sprite = sprite;
            var bgRect = bgImg.rectTransform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var fillGO = new GameObject("Fill", typeof(Image));
            fillGO.transform.SetParent(bgGO.transform, false);
            var fillImg = fillGO.GetComponent<Image>();
            fillImg.color = new Color(1f, 0.84f, 0f); // gold
            fillImg.type = Image.Type.Filled;
            fillImg.sprite = sprite;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = 0;
            fillImg.fillAmount = 1f;
            var fillRect = fillImg.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(bgGO.transform, false);
            instance.text = textGO.GetComponent<Text>();
            instance.text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            instance.text.alignment = TextAnchor.MiddleCenter;
            instance.text.color = Color.white;
            instance.text.fontSize = 11;
            var textRect = instance.text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            exp.OnLevelChanged += instance.HandleLevelChanged;
            instance.HandleLevelChanged(exp.Level);
        }

        /// <summary>
        /// Destroy the current pet level bar, if any.
        /// </summary>
        public static void DestroyInstance()
        {
            if (instance != null)
                Destroy(instance.gameObject);
        }

        private void HandleLevelChanged(int lvl)
        {
            UpdateLevelText();
        }

        private void UpdateLevelText()
        {
            if (text == null || experience == null)
                return;
            string tier = experience.TierName;
            if (string.IsNullOrEmpty(tier))
                text.text = $"Lv {experience.Level}";
            else
                text.text = $"{tier} Lv {experience.Level}";
        }

        public void ShowXpToNextLevel()
        {
            if (xpRoutine != null)
                StopCoroutine(xpRoutine);
            xpRoutine = StartCoroutine(ShowXpRoutine());
        }

        private IEnumerator ShowXpRoutine()
        {
            if (text == null || experience == null)
                yield break;
            int xp = experience.GetXpToNextLevel();
            text.text = xp > 0 ? $"{xp} XP till next lvl" : "Max level";
            yield return new WaitForSeconds(2f);
            UpdateLevelText();
            xpRoutine = null;
        }

        public void ToggleGuardMode()
        {
            PetDropSystem.GuardModeEnabled = !PetDropSystem.GuardModeEnabled;
        }

        public void ToggleInventory()
        {
            var pet = PetDropSystem.ActivePetObject;
            if (pet == null)
                return;
            var storage = pet.GetComponent<PetStorage>();
            if (storage == null)
                return;
            if (PetDropSystem.PetInventoryVisible)
            {
                storage.Close();
                PetDropSystem.PetInventoryVisible = false;
            }
            else
            {
                storage.Open();
                PetDropSystem.PetInventoryVisible = true;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                PetLevelBarMenu.Show(this, eventData.position);
        }

        private void OnDestroy()
        {
            if (experience != null)
                experience.OnLevelChanged -= HandleLevelChanged;
            if (instance == this)
                instance = null;
        }
    }
}
