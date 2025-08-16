using UnityEngine;
using UnityEngine.UI;

namespace Skills.Mining
{
    /// <summary>
    /// Displays mining progress above the current rock.
    /// </summary>
    public class MiningUI : MonoBehaviour
    {
        [SerializeField] private MiningSkill skill;
        [SerializeField] private Image progressPrefab;
        [SerializeField] private Vector3 offset = new Vector3(0f, 1.5f, 0f);

        private Transform target;
        private Image progressImage;

        private void Awake()
        {
            if (skill == null)
                skill = FindObjectOfType<MiningSkill>();

            if (skill != null)
            {
                skill.OnStartMining += HandleStart;
                skill.OnStopMining += HandleStop;
            }
        }

        private void HandleStart(MineableRock rock)
        {
            target = rock.transform;

            // Lazy instantiate the progress UI if needed.
            if (progressImage == null && progressPrefab != null)
            {
                progressImage = Instantiate(progressPrefab, transform);
                progressImage.transform.parent.gameObject.SetActive(false);
            }

            if (progressImage != null)
            {
                progressImage.fillAmount = 0f;
                progressImage.transform.parent.gameObject.SetActive(true);
            }
            else
            {
                Debug.LogWarning("MiningUI progress prefab not assigned.");
            }
        }

        private void HandleStop()
        {
            target = null;
            if (progressImage != null)
                progressImage.transform.parent.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (target == null || progressImage == null || skill == null)
                return;

            progressImage.transform.position = target.position + offset;
            progressImage.fillAmount = skill.SwingProgressNormalized;
        }

        private void OnDestroy()
        {
            if (skill != null)
            {
                skill.OnStartMining -= HandleStart;
                skill.OnStopMining -= HandleStop;
            }
        }
    }
}
