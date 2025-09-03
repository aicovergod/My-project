using System.Collections;
using UnityEngine;

namespace Skills.Fishing
{
    public class BycatchInitializer : MonoBehaviour
    {
        public BycatchTable table;
        public string resourcesPath = "FishingDatabase/ByCatch/BycatchTable";

        private void Start()
        {
            StartCoroutine(AssignWhenManagerReady());
        }

        private IEnumerator AssignWhenManagerReady()
        {
            BycatchManager manager;
            while ((manager = FindObjectOfType<BycatchManager>()) == null)
                yield return null;

            if (table == null)
                table = Resources.Load<BycatchTable>(resourcesPath);

            if (table != null)
                manager.bycatchTable = table;
            else
                Debug.LogWarning($"BycatchInitializer could not load BycatchTable at Resources/{resourcesPath}");
        }
    }
}
