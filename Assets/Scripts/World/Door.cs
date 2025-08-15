using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

namespace World
{
    /// <summary>
    /// Simple door interaction.  When the player clicks on the door the specified
    /// scene is loaded.  If a required item ID is provided the player must possess
    /// that item in their inventory to use the door.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Door : MonoBehaviour
    {
        [Tooltip("Name of the scene to load when this door is used.")]
        public string sceneToLoad;

        [Tooltip("Optional item ID required to use this door.  Leave empty for no requirement.")]
        public string requiredItemId;

        [Tooltip("Name of the spawn point in the target scene where the player should appear.")]
        public string spawnPointName;

        [Tooltip("How close the player must be in tiles to use the door.")]
        public float useRadius = 2f;

        private static string nextSpawnPoint;
        private static Transform playerToMove;
        private static GameObject cameraToMove;
        private static GameObject inventoryUIToMove;
        private static GameObject eventSystemToMove;

        private void Update()
        {
            if (!Input.GetMouseButtonDown(0))
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            var worldPoint = (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
            foreach (var col in Physics2D.OverlapPointAll(worldPoint))
            {
                if (col.gameObject == gameObject)
                {
                    StartCoroutine(UseDoor());
                    break;
                }
            }
        }

        private IEnumerator UseDoor()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) yield break;

            if (Vector2.Distance(player.transform.position, transform.position) > useRadius)
                yield break;

            Inventory.Inventory inv = player.GetComponent<Inventory.Inventory>();
            if (!string.IsNullOrEmpty(requiredItemId))
            {
                if (inv == null || !inv.HasItem(requiredItemId))
                {
                    // Player doesn't have the required item
                    yield break;
                }
            }

            if (!string.IsNullOrEmpty(sceneToLoad))
            {
                if (ScreenFader.Instance == null)
                    new GameObject("ScreenFader").AddComponent<ScreenFader>();

                if (ScreenFader.Instance != null)
                    yield return ScreenFader.Instance.FadeOut();

                nextSpawnPoint = spawnPointName;
                playerToMove = player.transform;
                DontDestroyOnLoad(player);

                var cam = Camera.main;
                cameraToMove = cam ? cam.gameObject : null;
                if (cameraToMove) DontDestroyOnLoad(cameraToMove);

                inventoryUIToMove = GameObject.Find("InventoryUI");
                if (inventoryUIToMove) DontDestroyOnLoad(inventoryUIToMove);

                var ev = EventSystem.current;
                eventSystemToMove = ev ? ev.gameObject : null;
                if (eventSystemToMove) DontDestroyOnLoad(eventSystemToMove);

                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.LoadScene(sceneToLoad);
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (playerToMove != null && !string.IsNullOrEmpty(nextSpawnPoint))
            {
                var points = GameObject.FindObjectsOfType<SpawnPoint>();
                foreach (var p in points)
                {
                    if (p.id == nextSpawnPoint)
                    {
                        playerToMove.position = p.transform.position;
                        break;
                    }
                }

                SceneManager.MoveGameObjectToScene(playerToMove.gameObject, scene);
                var players = GameObject.FindGameObjectsWithTag("Player");
                foreach (var p in players)
                {
                    if (p != playerToMove.gameObject)
                    {
                        Destroy(p);
                    }
                }
            }

            if (cameraToMove != null)
            {
                SceneManager.MoveGameObjectToScene(cameraToMove, scene);
                var cameras = GameObject.FindObjectsOfType<Camera>();
                foreach (var c in cameras)
                {
                    if (c.gameObject != cameraToMove)
                        Destroy(c.gameObject);
                }
            }

            if (inventoryUIToMove != null)
            {
                SceneManager.MoveGameObjectToScene(inventoryUIToMove, scene);
                var canvases = GameObject.FindObjectsOfType<Canvas>();
                foreach (var cv in canvases)
                {
                    if (cv.gameObject != inventoryUIToMove && cv.gameObject.name == inventoryUIToMove.name)
                        Destroy(cv.gameObject);
                }
            }

            if (eventSystemToMove != null)
            {
                SceneManager.MoveGameObjectToScene(eventSystemToMove, scene);
                var systems = GameObject.FindObjectsOfType<EventSystem>();
                foreach (var es in systems)
                {
                    if (es.gameObject != eventSystemToMove)
                        Destroy(es.gameObject);
                }
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            playerToMove = null;
            nextSpawnPoint = null;
            cameraToMove = null;
            inventoryUIToMove = null;
            eventSystemToMove = null;

            if (ScreenFader.Instance != null)
                ScreenFader.Instance.StartCoroutine(ScreenFader.Instance.FadeIn());
        }
    }
}
