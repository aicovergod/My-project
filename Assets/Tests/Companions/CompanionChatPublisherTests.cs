using System;
using System.Reflection;
using Companions;
using Companions.Chat;
using NUnit.Framework;
using UI.Chat;
using UnityEngine;

namespace Tests.Companions
{
    /// <summary>
    /// Validates the shared chat publishing helper to ensure companion dialogue routing
    /// remains consistent across call sites.
    /// </summary>
    public class CompanionChatPublisherTests
    {
        private GameObject chatObject;
        private GameObject companionObject;
        private FieldInfo controllerField;
        private ChatMessage lastMessage;
        private bool messageReceived;

        [SetUp]
        public void SetUp()
        {
            chatObject = new GameObject(nameof(ChatService));
            chatObject.AddComponent<ChatService>();

            var chatService = ChatService.Instance;
            Assert.IsNotNull(chatService, "Chat service should bootstrap during test setup.");

            chatService.MessageReceived += HandleMessageReceived;

            controllerField = typeof(CompanionManager).GetField("controller", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(controllerField, "Expected CompanionManager.controller field to remain discoverable.");
            controllerField.SetValue(null, null);

            companionObject = null;
            lastMessage = default;
            messageReceived = false;
        }

        [TearDown]
        public void TearDown()
        {
            var chatService = ChatService.Instance;
            if (chatService != null)
                chatService.MessageReceived -= HandleMessageReceived;

            if (controllerField != null)
                controllerField.SetValue(null, null);

            if (companionObject != null)
                UnityEngine.Object.DestroyImmediate(companionObject);

            if (chatObject != null)
                UnityEngine.Object.DestroyImmediate(chatObject);
        }

        [Test]
        public void TryPublish_WithInactiveCompanionRequirement_DoesNotEmitLine()
        {
            bool published = CompanionChatPublisher.TryPublish(() => "Hello", requireActiveCompanion: true);

            Assert.IsFalse(published, "Publishing should abort when no companion is active and the requirement is enforced.");
            Assert.IsFalse(messageReceived, "No chat events should fire when publishing is skipped.");
        }

        [Test]
        public void TryPublish_WithActiveCompanion_EnqueuesMessage()
        {
            ActivateCompanion();

            bool published = CompanionChatPublisher.TryPublish(() => "Hey there!", requireActiveCompanion: true);

            Assert.IsTrue(published, "Publishing should succeed when a companion is active and a non-empty line is produced.");
            Assert.IsTrue(messageReceived, "Chat event should fire for successful publications.");
            Assert.AreEqual(ChatChannel.Companion, lastMessage.Channel, "Helper should emit to the companion channel.");
            Assert.AreEqual("Companion", lastMessage.Sender, "Fallback companion name should be used when no definition is bound.");
            Assert.AreEqual("Hey there!", lastMessage.Text, "Resolved chat text should match the resolver output.");
        }

        [Test]
        public void TryPublish_WithWhitespaceLine_DoesNotPublish()
        {
            ActivateCompanion();

            bool published = CompanionChatPublisher.TryPublish(() => "   ");

            Assert.IsFalse(published, "Whitespace-only lines should be rejected.");
            Assert.IsFalse(messageReceived, "Whitespace lines must not emit chat messages.");
        }

        [Test]
        public void TryPublish_WithNullResolver_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CompanionChatPublisher.TryPublish(null));
        }

        private void ActivateCompanion()
        {
            companionObject = new GameObject("Companion_Test");
            var controller = companionObject.AddComponent<CompanionController>();
            controllerField.SetValue(null, controller);
        }

        private void HandleMessageReceived(ChatMessage message)
        {
            messageReceived = true;
            lastMessage = message;
        }
    }
}
