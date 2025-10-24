using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Companions;
using Companions.Conversation;
using NUnit.Framework;
using Skills;
using UnityEngine;

namespace Tests.Companions
{
    /// <summary>
    /// Covers behaviours provided by <see cref="CompanionConversationService"/> that need deterministic validation.
    /// </summary>
    public class CompanionConversationServiceTests
    {
        private GameObject serviceObject;
        private GameObject memoryObject;
        private CompanionConversationService service;
        private CompanionConversationMemory memory;

        [SetUp]
        public void SetUp()
        {
            // Ensure the dialogue catalog is initialised so the service bootstraps without null references.
            CompanionResponseCatalog.EnsureDefaults();

            memoryObject = new GameObject("ConversationMemory_Test");
            memory = memoryObject.AddComponent<CompanionConversationMemory>();

            serviceObject = new GameObject("ConversationService_Test");
            service = serviceObject.AddComponent<CompanionConversationService>();

            // Manually bind the freshly created memory to avoid relying on scene searches in tests.
            typeof(CompanionConversationService)
                .GetField("conversationMemory", BindingFlags.NonPublic | BindingFlags.Instance)?
                .SetValue(service, memory);
        }

        [TearDown]
        public void TearDown()
        {
            if (serviceObject != null)
                UnityEngine.Object.DestroyImmediate(serviceObject);
            if (memoryObject != null)
                UnityEngine.Object.DestroyImmediate(memoryObject);
        }

        [Test]
        public void ResolveRecentEventSummary_IgnoresLastStatusWhenNoEvents()
        {
            // Seed the conversation memory with a status response to mirror a previous status query.
            memory.RegisterStatusResponse("Holding up well.", DateTime.UtcNow);

            var method = typeof(CompanionConversationService).GetMethod(
                "ResolveRecentEventSummary",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, "ResolveRecentEventSummary should be discoverable via reflection.");

            string summary = (string)method.Invoke(service, null);
            Assert.IsEmpty(summary, "Expected no recent event summary when no gameplay events were recorded.");
        }

        [Test]
        public void MaybeScheduleProactiveQuestion_RespectsDeclineCooldowns()
        {
            GameObject controllerObject = null;

            try
            {
                controllerObject = new GameObject("CompanionController_Test");
                controllerObject.SetActive(false);

                // Create a lightweight controller and cooldown tracker without triggering save registration hooks.
                var controller = controllerObject.AddComponent<CompanionController>();
                var tracker = controllerObject.AddComponent<CompanionSkillCooldownTracker>();
                tracker.enabled = false;

                // Bind the tracker to the controller and expose it through CompanionManager.
                typeof(CompanionController)
                    .GetField("skillCooldownTracker", BindingFlags.NonPublic | BindingFlags.Instance)?
                    .SetValue(controller, tracker);

                typeof(CompanionManager)
                    .GetField("controller", BindingFlags.NonPublic | BindingFlags.Static)?
                    .SetValue(null, controller);

                // Seed an active mining cooldown by writing directly into the internal tracker dictionary.
                var cooldownField = typeof(CompanionSkillCooldownTracker)
                    .GetField("cooldownExpiryTicks", BindingFlags.NonPublic | BindingFlags.Instance);
                var cooldowns = (Dictionary<SkillType, long>)cooldownField.GetValue(tracker);
                DateTime nowUtc = DateTime.UtcNow;
                cooldowns[SkillType.Mining] = nowUtc.AddMinutes(5).Ticks;

                // Register a mining skill candidate so the scheduler has a proactive option to consider.
                var registerMethod = typeof(CompanionConversationService).GetMethod(
                    "RegisterSkillEventCandidate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var metadata = CompanionEventMetadata.Create(
                    primaryActor: "TestPlayer",
                    skill: SkillType.Mining,
                    additionalContext: "Pulled a shiny ore");
                registerMethod.Invoke(service, new object[] { "Mined some ore", metadata });

                // Prime the idle counter so the next scheduler tick will attempt to queue a question immediately.
                var idleField = typeof(CompanionConversationService)
                    .GetField("idleTickCounter", BindingFlags.NonPublic | BindingFlags.Instance);
                var thresholdField = typeof(CompanionConversationService)
                    .GetField("proactiveIdleTickThreshold", BindingFlags.NonPublic | BindingFlags.Instance);
                int threshold = Mathf.Max(1, (int)thresholdField.GetValue(service));
                idleField.SetValue(service, threshold - 1);

                var maybeScheduleMethod = typeof(CompanionConversationService).GetMethod(
                    "MaybeScheduleProactiveQuestion",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                // Invoke the scheduler while the cooldown is active and verify nothing is queued.
                maybeScheduleMethod.Invoke(service, new object[] { nowUtc });

                var queueField = typeof(CompanionConversationService)
                    .GetField("pendingResponses", BindingFlags.NonPublic | BindingFlags.Instance);
                var queue = (ICollection)queueField.GetValue(service);

                Assert.IsNotNull(queue, "Pending response queue should be accessible for validation.");
                Assert.AreEqual(0, queue.Count, "Expected no proactive prompt while the decline cooldown is active.");

                // Remove the cooldown entry to simulate the timer expiring naturally.
                cooldowns.Remove(SkillType.Mining);

                // Re-prime the idle counter and rerun the scheduler so the candidate can be surfaced.
                nowUtc = DateTime.UtcNow.AddMinutes(1);
                idleField.SetValue(service, threshold - 1);
                maybeScheduleMethod.Invoke(service, new object[] { nowUtc });

                Assert.Greater(queue.Count, 0, "Expected the proactive prompt to queue once the cooldown expired.");
            }
            finally
            {
                typeof(CompanionManager)
                    .GetField("controller", BindingFlags.NonPublic | BindingFlags.Static)?
                    .SetValue(null, null);

                if (controllerObject != null)
                    UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }
    }
}
