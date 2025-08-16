using System;
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

        public event Action OnTick;

        private float timer;

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

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer >= TickDuration)
            {
                timer -= TickDuration;
                OnTick?.Invoke();
            }
        }
    }
}
