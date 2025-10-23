using System;
using System.Reflection;
using Companions.Conversation;
using NUnit.Framework;
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
    }
}
