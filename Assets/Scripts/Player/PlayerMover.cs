// Assets/Scripts/Player/PlayerMover.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using Core.Save;
using World;
using Pets;
using Util;
using Status.Freeze;
using Player.Input;
using Player.Movement;
using Player.Visuals;

namespace Player
{
    [RequireComponent(typeof(PlayerMovementController))]
    [RequireComponent(typeof(PlayerMovementInput))]
    [RequireComponent(typeof(PlayerSpriteController))]
    [RequireComponent(typeof(FrozenStatusController))]
    public class PlayerMover : ScenePersistentObject, ISaveable
    {
        [Header("Movement Components")]
        [SerializeField, Tooltip("Handles physics-driven locomotion and persistence timers.")]
        private PlayerMovementController movementController;

        [SerializeField, Tooltip("Resolves player movement input via the shared action map.")]
        private PlayerMovementInput movementInput;

        [SerializeField, Tooltip("Drives sprite overrides and facing visuals.")]
        private PlayerSpriteController spriteController;

        [HideInInspector]
        public bool CanDrop = true;

        private GameObject petToMove;
        private bool isTransitioning;
        private bool registeredWithSaveManager;

        private static PlayerMover instance;

        [Serializable]
        private class PositionData
        {
            public float x;
            public float y;
            public float z;
            public string scene;
        }

        private const string PositionKey = "PlayerPosition";

        protected override void Awake()
        {
            base.Awake();

            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            if (movementController == null)
                movementController = GetComponent<PlayerMovementController>();
            if (movementInput == null)
                movementInput = GetComponent<PlayerMovementInput>();
            if (spriteController == null)
                spriteController = GetComponent<PlayerSpriteController>();

            SaveManager.Register(this);
            registeredWithSaveManager = true;

            if (movementController != null)
                movementController.MovementSaveRequested += HandleMovementSaveRequested;

            SceneTransitionManager.TransitionStarted += OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted += OnTransitionCompleted;
        }

        private void OnEnable()
        {
            SceneTransitionManager.RegisterPersistentObject(this);
        }

        private void OnDisable()
        {
            SceneTransitionManager.UnregisterPersistentObject(this);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            SceneTransitionManager.TransitionStarted -= OnTransitionStarted;
            SceneTransitionManager.TransitionCompleted -= OnTransitionCompleted;

            if (movementController != null)
                movementController.MovementSaveRequested -= HandleMovementSaveRequested;

            if (registeredWithSaveManager)
            {
                SaveManager.Unregister(this);
                registeredWithSaveManager = false;
            }
        }

        /// <summary>Current facing direction provided by the movement controller.</summary>
        public Direction8 FacingDir => movementController != null ? movementController.FacingDirection : Direction8.Down;

        /// <summary>True while the movement controller is executing an auto-move routine.</summary>
        public bool IsAutoMoving => movementController != null && movementController.IsAutoMoving;

        /// <summary>True while the movement controller reports active displacement from player input or auto-walk.</summary>
        public bool IsMoving => movementController != null && movementController.IsMoving;

        /// <summary>True while movement input is frozen by external systems.</summary>
        public bool IsMovementFrozen => movementController != null && movementController.IsMovementFrozen;

        /// <summary>Exposes the underlying movement controller for systems that require direct access.</summary>
        public PlayerMovementController MovementController => movementController;

        /// <summary>Exposes the sprite controller responsible for directional art overrides.</summary>
        public PlayerSpriteController SpriteController => spriteController;

        /// <summary>Exposes the input provider responsible for resolving the Move action.</summary>
        public PlayerMovementInput MovementInput => movementInput;

        /// <summary>Delegates to the movement controller to freeze or unfreeze locomotion.</summary>
        public void SetMovementFrozen(bool frozen)
        {
            movementController?.SetMovementFrozen(frozen);
        }

        /// <summary>Immediately halts all movement via the movement controller.</summary>
        public void StopMovement()
        {
            movementController?.StopMovement();
        }

        /// <summary>Faces the supplied target using the movement controller.</summary>
        public void FaceTarget(Transform target)
        {
            movementController?.FaceTarget(target);
        }

        /// <summary>Begins an auto-move towards a world position.</summary>
        public void MoveTo(Vector2 target, float stopDistance, Action onComplete = null)
        {
            movementController?.MoveTo(target, stopDistance, onComplete);
        }

        /// <summary>Begins an auto-move towards a transform.</summary>
        public void MoveTo(Transform target, float stopDistance, Action onComplete = null)
        {
            movementController?.MoveTo(target, stopDistance, onComplete);
        }

        private void OnTransitionStarted()
        {
            isTransitioning = true;
            movementController?.SetTransitioning(true);
        }

        private void OnTransitionCompleted()
        {
            isTransitioning = false;
            movementController?.SetTransitioning(false);
        }

        public void SavePosition()
        {
            Vector3 pos = transform.position;
            var data = new PositionData
            {
                x = pos.x,
                y = pos.y,
                z = pos.z,
                scene = SceneManager.GetActiveScene().name
            };
            SaveManager.Save(PositionKey, data);
            SaveManager.UpdateLastKnownLocation(data.scene, pos);
        }

        public void Save()
        {
            SavePosition();
        }

        public void Load()
        {
            LoadPosition();
        }

        private void LoadPosition()
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data == null)
                return;
            if (isTransitioning)
                return;
            if (SceneTransitionManager.IsTransitioning)
                return;

            if (SceneManager.GetActiveScene().name == data.scene)
            {
                ApplySavedPosition();

                var pet = PetDropSystem.ActivePetObject;
                if (pet != null)
                {
                    pet.transform.position = transform.position;
                    var follower = pet.GetComponent<PetFollower>();
                    if (follower != null)
                        follower.SetPlayer(transform);
                }
            }
            else
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                var pet = PetDropSystem.ActivePetObject;
                if (pet != null)
                {
                    petToMove = pet;
                    DontDestroyOnLoad(pet);
                }
                SceneManager.LoadScene(data.scene);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data != null && scene.name == data.scene)
            {
                ApplySavedPosition();
                if (petToMove != null)
                {
                    petToMove.transform.position = transform.position;
                    var follower = petToMove.GetComponent<PetFollower>();
                    if (follower != null)
                        follower.SetPlayer(transform);
                    SceneManager.MoveGameObjectToScene(petToMove, scene);
                    petToMove = null;
                }
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private bool ApplySavedPosition()
        {
            var data = SaveManager.Load<PositionData>(PositionKey);
            if (data == null)
                return false;

            transform.position = new Vector3(data.x, data.y, data.z);
            return true;
        }

        public override void OnAfterSceneLoad(Scene scene)
        {
            base.OnAfterSceneLoad(scene);

            bool positionedFromSpawn = false;
            bool positionedFromSave = false;

            var spawnId = SceneTransitionManager.NextSpawnPoint;
            bool spawnRequested = !string.IsNullOrEmpty(spawnId);
            if (spawnRequested)
            {
                var points = GameObject.FindObjectsOfType<SpawnPoint>();
                foreach (var p in points)
                {
                    if (p.id == spawnId)
                    {
                        transform.position = p.transform.position;
                        positionedFromSpawn = true;
                        break;
                    }
                }

                if (!positionedFromSpawn)
                {
                    Debug.LogWarning($"SceneTransitionManager requested spawn point '{spawnId}' but no matching SpawnPoint was found in scene '{scene.name}'. Falling back to saved position if available.", this);
                }
            }

            if (!positionedFromSpawn)
                positionedFromSave = ApplySavedPosition();

            bool shouldPersistPosition = positionedFromSpawn || positionedFromSave;

            var activePet = PetDropSystem.ActivePetObject;
            if (activePet != null)
            {
                activePet.transform.position = transform.position;
                var follower = activePet.GetComponent<PetFollower>();
                if (follower != null)
                    follower.SetPlayer(transform);

                if (activePet.scene != scene)
                    SceneManager.MoveGameObjectToScene(activePet, scene);
            }

            var players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var p in players)
            {
                if (p != gameObject)
                    Destroy(p);
            }

            var myListener = GetComponentInChildren<AudioListener>();
            if (myListener != null)
            {
                var listeners = GameObject.FindObjectsOfType<AudioListener>();
                foreach (var l in listeners)
                {
                    if (l != myListener)
                        Destroy(l);
                }
            }

            if (shouldPersistPosition)
                SavePosition();
        }

        private void HandleMovementSaveRequested()
        {
            SavePosition();
        }

        private void OnApplicationQuit()
        {
            SavePosition();
        }
    }
}
