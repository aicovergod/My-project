using System;
using UnityEngine;
using UnityEngine.UI;
using Beastmaster;
using Object = UnityEngine.Object;

namespace Pets
{
    /// <summary>
    /// Extends the pet level bar context menu with Merge/Unmerge options.
    /// </summary>
    public partial class PetLevelBarMenu
    {
        private Button mergeButton;
        private Text mergeText;
        private PetMergeController mergeController;

        partial void OnMenuCreated(Transform menuRoot)
        {
            mergeButton = CreateButton(menuRoot, "Merge");
            mergeText = mergeButton.GetComponentInChildren<Text>();
            mergeButton.onClick.AddListener(OnMergeClicked);
        }

        partial void OnMenuShown()
        {
            if (mergeButton == null)
                return;
            if (mergeController == null)
                mergeController = Object.FindObjectOfType<PetMergeController>();
            if (mergeController == null)
            {
                mergeButton.gameObject.SetActive(false);
                return;
            }
            mergeButton.gameObject.SetActive(true);
            if (mergeController.IsMerged)
            {
                mergeText.text = "Unmerge";
                mergeButton.interactable = true;
            }
            else if (mergeController.IsOnCooldown)
            {
                TimeSpan cd = TimeSpan.FromSeconds(mergeController.CooldownRemaining);
                mergeText.text = $"Merge ({cd.Minutes:00}:{cd.Seconds:00})";
                mergeButton.interactable = false;
            }
            else if (!mergeController.CanMerge)
            {
                mergeText.text = "Merge";
                mergeButton.interactable = false;
            }
            else
            {
                mergeText.text = "Merge";
                mergeButton.interactable = true;
            }
        }

        private void OnMergeClicked()
        {
            if (mergeController == null)
                return;
            if (mergeController.IsMerged)
                mergeController.EndMerge();
            else
                mergeController.TryStartMerge();
            OnMenuShown();
        }
    }
}
