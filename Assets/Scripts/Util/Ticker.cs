using System.Collections.Generic;
using UnityEngine;

namespace Util
{
    /// <summary>
    /// Fires an event every OSRS-style tick (0.6 seconds).
    /// </summary>
    public class Ticker : MonoBehaviour
    {
        public const float TickDuration = 0.6f;

        public static Ticker Instance { get; private set; }

        private readonly List<ITickable> subscribers = new List<ITickable>();
        private float timer;

        [SerializeField]
        private bool logTicks;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("Ticker initialized");
        }

        private void Update()
        {
            timer += Time.deltaTime;
            while (timer >= TickDuration)
            {
                timer -= TickDuration;
                if (logTicks)
                {
                    Debug.Log("Tick");
                }
                for (int i = 0; i < subscribers.Count; i++)
                {
                    subscribers[i]?.OnTick();
                }
            }
        }

        /// <summary>
        /// Registers a tickable object so it receives future <see cref="ITickable.OnTick"/> calls.
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
        /// </summary>
        /// <param name="tickable">The object to unregister.</param>
        public void Unsubscribe(ITickable tickable)
        {
            if (tickable != null)
            {
                subscribers.Remove(tickable);
            }
        }
    }
}
