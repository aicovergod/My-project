using System.Collections.Generic;
using UnityEngine;
using World;

namespace Util
{
    /// <summary>
    /// Fires an event every OSRS-style tick (0.6 seconds).
    /// </summary>
    public class Ticker : ScenePersistentObject
    {
        public const float TickDuration = 0.6f;

        public static Ticker Instance { get; private set; }

        private readonly List<ITickable> subscribers = new List<ITickable>();
        private float timer;
        private bool paused;

        [SerializeField]
        private bool logTicks;

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            base.Awake();

            Instance = this;
            Debug.Log("Ticker initialized");
        }

        private void Update()
        {
            if (paused)
            {
                return;
            }

            timer += Time.deltaTime;
            while (timer >= TickDuration)
            {
                timer -= TickDuration;
                if (logTicks)
                {
                    Debug.Log("Tick");
                }
                var snapshot = subscribers.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    snapshot[i]?.OnTick();
                }
            }
        }

        /// <summary>
        /// Registers a tickable object so it receives future <see cref="ITickable.OnTick"/> calls.
        /// Safe to call from within <see cref="ITickable.OnTick"/>.
        /// </summary>
        /// <param name="tickable">The object to register.</param>
        public void Subscribe(ITickable tickable)
        {
            if (tickable != null && !subscribers.Contains(tickable))
            {
                subscribers.Add(tickable);
            }
        }

        /// <summary>
        /// Removes a previously registered tickable object.
        /// Safe to call from within <see cref="ITickable.OnTick"/>.
        /// </summary>
        /// <param name="tickable">The object to unregister.</param>
        public void Unsubscribe(ITickable tickable)
        {
            if (tickable != null)
            {
                subscribers.Remove(tickable);
            }
        }

        /// <summary>
        /// Stops ticking until <see cref="Resume"/> is called.
        /// </summary>
        public void Pause()
        {
            paused = true;
        }

        /// <summary>
        /// Resumes ticking after a call to <see cref="Pause"/>.
        /// </summary>
        public void Resume()
        {
            paused = false;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Pause();
            }
            else
            {
                Resume();
            }
        }
    }
}
