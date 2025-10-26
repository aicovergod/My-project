using System;
using System.Reflection;
using Companions;
using NUnit.Framework;
using Player;
using UI.Chat;
using UnityEngine;

namespace Tests.Player
{
    /// <summary>
    /// Ensures <see cref="PlayerRespawnSystem"/> escalates to supportive companion dialogue after repeated deaths.
    /// </summary>
    public class PlayerRespawnSystemToneTests
    {
        private GameObject respawnObject;
        private PlayerRespawnSystem respawnSystem;
        private GameObject chatObject;
        private GameObject companionObject;
        private FieldInfo controllerField;
        private FieldInfo selectorField;
        private Func<CompanionChatTone, string> originalSelector;

        [SetUp]
        public void SetUp()
        {
            respawnObject = new GameObject(nameof(PlayerRespawnSystem));
            respawnSystem = respawnObject.AddComponent<PlayerRespawnSystem>();

            chatObject = new GameObject(nameof(ChatService));
            chatObject.AddComponent<ChatService>();
            _ = ChatService.Instance;

            companionObject = new GameObject("Companion");
            var companionController = companionObject.AddComponent<CompanionController>();

            controllerField = typeof(CompanionManager).GetField("controller", BindingFlags.NonPublic | BindingFlags.Static);
            controllerField.SetValue(null, companionController);

            selectorField = typeof(PlayerRespawnSystem).GetField("playerDeathLineSelector", BindingFlags.NonPublic | BindingFlags.Static);
            originalSelector = (Func<CompanionChatTone, string>)selectorField.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            if (selectorField != null)
                selectorField.SetValue(null, originalSelector ?? CompanionChatLibrary.GetRandomPlayerDeathLine);

            if (controllerField != null)
                controllerField.SetValue(null, null);

            if (respawnObject != null)
                UnityEngine.Object.DestroyImmediate(respawnObject);

            if (chatObject != null)
                UnityEngine.Object.DestroyImmediate(chatObject);

            if (companionObject != null)
                UnityEngine.Object.DestroyImmediate(companionObject);
        }

        [Test]
        public void RegisterDeathForToneEvaluation_SupportiveAfterRapidDeaths()
        {
            var method = typeof(PlayerRespawnSystem).GetMethod("RegisterDeathForToneEvaluation", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "Expected helper method to exist for tone evaluation.");

            var toneOne = (CompanionChatTone)method.Invoke(respawnSystem, new object[] { 10f });
            var toneTwo = (CompanionChatTone)method.Invoke(respawnSystem, new object[] { 20f });
            var toneThree = (CompanionChatTone)method.Invoke(respawnSystem, new object[] { 25f });

            Assert.AreEqual(CompanionChatTone.Snarky, toneOne, "First death should remain snarky.");
            Assert.AreEqual(CompanionChatTone.Snarky, toneTwo, "Second death inside the window should still be snarky.");
            Assert.AreEqual(CompanionChatTone.Supportive, toneThree, "Third rapid death should trigger the supportive tone.");
        }

        [Test]
        public void TryPublishCompanionDeathLine_InvokesSelectorWithResolvedTone()
        {
            CompanionChatTone capturedTone = CompanionChatTone.Snarky;
            selectorField.SetValue(null, new Func<CompanionChatTone, string>(tone =>
            {
                capturedTone = tone;
                return "Test line";
            }));

            var method = typeof(PlayerRespawnSystem).GetMethod("TryPublishCompanionDeathLine", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "Expected chat publishing helper to remain accessible.");

            method.Invoke(null, new object[] { CompanionChatTone.Supportive });

            Assert.AreEqual(CompanionChatTone.Supportive, capturedTone, "Selector should receive the resolved supportive tone.");
        }
    }
}
