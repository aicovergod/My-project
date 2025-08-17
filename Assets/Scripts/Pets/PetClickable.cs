using System.Collections;
using UnityEngine;
using Inventory;

namespace Pets
{
    /// <summary>
    /// Detects clicks on the pet and converts it to an inventory item.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class PetClickable : MonoBehaviour
    {
        private PetDefinition definition;

        public void Init(PetDefinition def)
        {
            definition = def;
        }

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Vector3 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                Vector2 p = new Vector2(world.x, world.y);
                var hit = Physics2D.OverlapPoint(p);
                if (hit != null && hit.gameObject == gameObject)
                    OnClicked();
            }
        }

        private void OnClicked()
        {
            if (definition != null && definition.pickupItem != null)
                InventoryBridge.AddItem(definition.pickupItem, 1);

            PetDropSystem.DespawnActive();
            PetToastUI.Show("You pick up the pet.");
            StartCoroutine(FadeText());
            Destroy(gameObject);
        }

        private IEnumerator FadeText()
        {
            var go = new GameObject("PetPickupText");
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = "You pick up the pet.";
            text.color = Color.white;
            text.characterSize = 0.1f;
            text.anchor = TextAnchor.MiddleCenter;
            go.transform.position = transform.position + Vector3.up * 0.5f;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 2f;
                go.transform.position += Vector3.up * Time.deltaTime * 0.5f;
                var c = text.color;
                c.a = 1f - t;
                text.color = c;
                yield return null;
            }

            Destroy(go);
        }
    }
}