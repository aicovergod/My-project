using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using Pets;

namespace World
{
    /// <summary>
    /// Manages moving key objects between scenes and handling fade transitions.
    /// </summary>
    public class SceneTransitionManager : MonoBehaviour
    {
        public static SceneTransitionManager Instance;
        public static bool IsTransitioning;

        private Transform _playerToMove;
        private GameObject _cameraToMove;
        private GameObject _inventoryUIToMove;
        private GameObject _questUIToMove;
        private GameObject _eventSystemToMove;
        private GameObject _petToMove;
        private string _nextSpawnPoint;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public IEnumerator Transition(string sceneToLoad, string spawnPointName, string requiredItemId, bool removeItemOnUse)
        {
            if (string.IsNullOrEmpty(sceneToLoad))
                yield break;

            if (ScreenFader.Instance == null)
                new GameObject("ScreenFader").AddComponent<ScreenFader>();

            if (ScreenFader.Instance != null)
                yield return ScreenFader.Instance.FadeOut();

            _nextSpawnPoint = spawnPointName;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                _playerToMove = player.transform;
                var inv = player.GetComponent<Inventory.Inventory>();
                if (removeItemOnUse && inv != null && !string.IsNullOrEmpty(requiredItemId))
                    inv.RemoveItem(requiredItemId);
                DontDestroyOnLoad(player);
            }

            var cam = Camera.main;
            _cameraToMove = cam ? cam.gameObject : null;
            if (_cameraToMove) DontDestroyOnLoad(_cameraToMove);

            _inventoryUIToMove = GameObject.Find("InventoryUI");
            if (_inventoryUIToMove) DontDestroyOnLoad(_inventoryUIToMove);

            _questUIToMove = GameObject.Find("QuestUI");
            if (_questUIToMove) DontDestroyOnLoad(_questUIToMove);

            var ev = EventSystem.current;
            _eventSystemToMove = ev ? ev.gameObject : null;
            if (_eventSystemToMove) DontDestroyOnLoad(_eventSystemToMove);

            var pet = PetDropSystem.ActivePetObject;
            if (pet != null)
            {
                _petToMove = pet;
                DontDestroyOnLoad(pet);
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            if (_eventSystemToMove)
                // Temporarily disable to avoid multiple active EventSystems during load
                _eventSystemToMove.SetActive(false);

            IsTransitioning = true;
            SceneManager.LoadScene(sceneToLoad);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_playerToMove != null && !string.IsNullOrEmpty(_nextSpawnPoint))
            {
                var points = GameObject.FindObjectsOfType<SpawnPoint>();
                foreach (var p in points)
                {
                    if (p.id == _nextSpawnPoint)
                    {
                        _playerToMove.position = p.transform.position;
                        break;
                    }
                }

                SceneManager.MoveGameObjectToScene(_playerToMove.gameObject, scene);
                var players = GameObject.FindGameObjectsWithTag("Player");
                foreach (var p in players)
                {
                    if (p != _playerToMove.gameObject)
                        Destroy(p);
                }

                var playerMover = _playerToMove.GetComponent<Player.PlayerMover>();
                if (playerMover != null)
                    playerMover.SavePosition();
            }

            if (_cameraToMove != null)
            {
                SceneManager.MoveGameObjectToScene(_cameraToMove, scene);
                var cameras = GameObject.FindObjectsOfType<Camera>();
                foreach (var c in cameras)
                {
                    var isMinimapCam = c.GetComponentInParent<Minimap>() != null;
                    Debug.Log($"[SceneTransition] Found camera {c.name}, isMinimap={isMinimapCam}");
                    if (c.gameObject != _cameraToMove && !isMinimapCam)
                    {
                        Debug.Log($"[SceneTransition] Destroying camera {c.name}");
                        Destroy(c.gameObject);
                    }
                }
            }

            if (_inventoryUIToMove != null)
            {
                SceneManager.MoveGameObjectToScene(_inventoryUIToMove, scene);
                var canvases = GameObject.FindObjectsOfType<Canvas>();
                foreach (var cv in canvases)
                {
                    if (cv.gameObject != _inventoryUIToMove && cv.gameObject.name == _inventoryUIToMove.name)
                        Destroy(cv.gameObject);
                }
            }

            if (_questUIToMove != null)
            {
                SceneManager.MoveGameObjectToScene(_questUIToMove, scene);
                var canvases = GameObject.FindObjectsOfType<Canvas>();
                foreach (var cv in canvases)
                {
                    if (cv.gameObject != _questUIToMove && cv.gameObject.name == _questUIToMove.name)
                        Destroy(cv.gameObject);
                }
            }

            if (_eventSystemToMove != null)
            {
                SceneManager.MoveGameObjectToScene(_eventSystemToMove, scene);
                // Remove any additional EventSystems, even if they are disabled
                var systems = GameObject.FindObjectsOfType<EventSystem>(true);
                foreach (var es in systems)
                {
                    if (es.gameObject != _eventSystemToMove)
                        Destroy(es.gameObject);
                }
                // Reactivate after removing any duplicates
                _eventSystemToMove.SetActive(true);
            }

            if (_petToMove != null)
            {
                if (_playerToMove != null)
                {
                    _petToMove.transform.position = _playerToMove.position;
                    var follower = _petToMove.GetComponent<PetFollower>();
                    if (follower != null)
                        follower.SetPlayer(_playerToMove);
                }
                SceneManager.MoveGameObjectToScene(_petToMove, scene);
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            _playerToMove = null;
            _nextSpawnPoint = null;
            _cameraToMove = null;
            _inventoryUIToMove = null;
            _questUIToMove = null;
            _eventSystemToMove = null;
            _petToMove = null;

            if (ScreenFader.Instance != null)
                ScreenFader.Instance.StartCoroutine(ScreenFader.Instance.FadeIn());

            IsTransitioning = false;
        }
    }
}
