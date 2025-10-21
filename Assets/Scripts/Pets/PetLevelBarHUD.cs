using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using Player;
using UI;
using Companions;

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

        /// <summary>True when the HUD currently represents the companion.</summary>
        private bool isCompanion;

        /// <summary>Tracks whether companion event subscriptions have been established.</summary>
        private bool companionEventsBound;

        /// <summary>
        /// Create the pet level bar under the existing health bar.
        /// If a bar already exists it will be replaced.
        /// </summary>
        public static void CreateForPet(PetExperience exp)
        {
            if (exp == null)
                return;

            var hud = BuildHudSkeleton();
            if (hud == null)
                return;

            hud.experience = exp;
            hud.isCompanion = false;
            exp.OnLevelChanged += hud.HandleLevelChanged;
            hud.HandleLevelChanged(exp.Level);
        }

        /// <summary>
        /// Creates the level bar for the companion. Returns the created HUD so the manager can wire events.
        /// </summary>
        public static PetLevelBarHUD CreateForCompanion()
        {
            var hud = BuildHudSkeleton();
            if (hud == null)
                return null;

            hud.experience = null;
            hud.isCompanion = true;
            hud.UpdateLevelText();
            hud.BindToCompanion();
            return hud;
        }

        /// <summary>
        /// Destroy the current pet level bar, if any.
        /// </summary>
        public static void DestroyInstance()
        {
            if (instance != null)
                Destroy(instance.gameObject);
        }

        /// <summary>
        /// Indicates whether this HUD currently represents the companion instead of a standard pet.
        /// </summary>
        public bool IsCompanionHud => isCompanion;

        /// <summary>
        /// Ensures the HUD is bound to the companion event stream so combat levels stay in sync.
        /// </summary>
        internal void BindToCompanion()
        {
            if (!isCompanion || companionEventsBound)
                return;

            isCompanion = true;
            companionEventsBound = true;
            CompanionManager.RegisterHud(this);
            CompanionManager.CombatLevelChanged += HandleCompanionCombatLevelChanged;
            UpdateLevelText();
        }

        private static PetLevelBarHUD BuildHudSkeleton()
        {
            if (instance != null)
                Destroy(instance.gameObject);

            var healthHud = Object.FindObjectOfType<HealthHUD>();
            if (healthHud == null)
                return null;

            var healthRect = healthHud.GetComponent<RectTransform>();
            var parent = healthRect.parent as RectTransform;
            var go = new GameObject("PetLevelHUD", typeof(RectTransform), typeof(PetLevelBarHUD));
            var hud = go.GetComponent<PetLevelBarHUD>();
            instance = hud;
            hud.experience = null;
            hud.isCompanion = false;
            hud.companionEventsBound = false;
            hud.xpRoutine = null;
            go.transform.SetParent(parent, false);

            var sprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = healthRect.anchorMin;
            rect.anchorMax = healthRect.anchorMax;
            rect.pivot = healthRect.pivot;
            rect.sizeDelta = new Vector2(300f, 30f);
            rect.anchoredPosition = new Vector2(-10f, -348f);

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
            fillImg.color = new Color(1f, 0.84f, 0f);
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
            hud.text = textGO.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(hud.text);
            hud.text.alignment = TextAnchor.MiddleCenter;
            hud.text.color = Color.white;
            hud.text.fontSize = 24;
            var outline = textGO.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
            var textRect = hud.text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return hud;
        }

        private void HandleLevelChanged(int lvl)
        {
            UpdateLevelText();
        }

        private void HandleCompanionCombatLevelChanged(int level)
        {
            UpdateLevelText();
        }

        private void UpdateLevelText()
        {
            if (text == null)
                return;

            if (isCompanion)
            {
                text.text = $"Combat lvl {CompanionManager.CombatLevel}";
                return;
            }

            if (experience == null)
                return;

            string tier = experience.TierName;
            if (string.IsNullOrEmpty(tier))
                text.text = $"Lv {experience.Level}";
            else
                text.text = $"{tier} Lv {experience.Level}";
        }

        public void ShowXpToNextLevel()
        {
            if (isCompanion)
            {
                CompanionManager.OpenStats();
                return;
            }

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
            if (isCompanion)
            {
                CompanionManager.ToggleGuardMode();
                return;
            }

            PetDropSystem.GuardModeEnabled = !PetDropSystem.GuardModeEnabled;
        }

        public void ToggleInventory()
        {
            if (isCompanion)
            {
                CompanionManager.ToggleInventory();
                return;
            }

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
            if (companionEventsBound)
            {
                CompanionManager.CombatLevelChanged -= HandleCompanionCombatLevelChanged;
                CompanionManager.UnbindHud(this);
                companionEventsBound = false;
            }
            if (xpRoutine != null)
                StopCoroutine(xpRoutine);
            if (instance == this)
                instance = null;
        }
    }
}
