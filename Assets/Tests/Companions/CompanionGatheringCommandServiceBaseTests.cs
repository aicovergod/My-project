using System;
using Companions;
using Companions.Commands;
using NUnit.Framework;

namespace Tests.Companions
{
    /// <summary>
    /// Verifies that <see cref="CompanionGatheringCommandServiceBase"/> correctly populates router requests
    /// so derived services only need to provide skill-specific delegates.
    /// </summary>
    public sealed class CompanionGatheringCommandServiceBaseTests
    {
        /// <summary>
        /// Simple controller used to satisfy the generic constraints in the test helper.
        /// </summary>
        private sealed class DummyController
        {
        }

        /// <summary>
        /// Result enum used by the test helper.
        /// </summary>
        private enum DummyResult
        {
            Success,
            Failure,
            Cooldown
        }

        /// <summary>
        /// Test double that exposes the protected members from <see cref="CompanionGatheringCommandServiceBase"/>.
        /// </summary>
        private sealed class TestableGatheringService : CompanionGatheringCommandServiceBase
        {
            public TestableGatheringService(string skillName, string logPrefix = "[Companion]")
                : base(skillName, logPrefix)
            {
            }

            public CompanionSkillCommandRouter.SingleTargetCommandRequest<DummyController, DummyResult> BuildSingleTargetRequest(
                CompanionSkillCommandGuard.CooldownCheck<DummyResult> cooldownCheck,
                CompanionSkillCommandGuard.SingleCommandExecutor<DummyController, DummyResult> commandExecutor,
                Func<DummyResult, bool> treatResultAsSuccess,
                Func<DummyResult, string> cooldownMessageBuilder,
                Func<bool, DummyResult, string> outcomeMessageBuilder,
                Action<DummyResult> onFailure,
                Action<DummyResult> onSuccess,
                Func<DummyResult, bool> shouldAttemptInventoryFallback,
                Func<bool> inventoryFallback,
                CompanionSkillCommandGuard.SingleCommandExecutor<DummyController, DummyResult> fallbackExecutor,
                Action<DummyResult> resultObserver)
            {
                return CreateSingleTargetRequest(
                    cooldownCheck,
                    commandExecutor,
                    treatResultAsSuccess,
                    cooldownMessageBuilder,
                    outcomeMessageBuilder,
                    onFailure,
                    onSuccess,
                    shouldAttemptInventoryFallback,
                    inventoryFallback,
                    fallbackExecutor,
                    resultObserver);
            }

            public CompanionSkillCommandRouter.AreaCommandRequest<DummyController, DummyResult> BuildAreaRequest(
                DummyResult guardRejectionResult,
                CompanionSkillCommandGuard.CooldownCheck<DummyResult> cooldownCheck,
                CompanionSkillCommandGuard.AreaCommandExecutor<DummyController, DummyResult> commandExecutor,
                Func<DummyResult, bool> treatFailureAsSuccess,
                Func<float, DummyResult, string> cooldownMessageBuilder,
                Func<bool, DummyResult, float, string> outcomeMessageBuilder,
                Action<DummyResult> onFailure,
                Action<DummyResult> onSuccess,
                Func<DummyResult, bool> shouldAttemptInventoryFallback,
                Func<bool> inventoryFallback,
                CompanionSkillCommandGuard.AreaCommandExecutor<DummyController, DummyResult> fallbackExecutor)
            {
                return CreateAreaCommandRequest(
                    guardRejectionResult,
                    cooldownCheck,
                    commandExecutor,
                    treatFailureAsSuccess,
                    cooldownMessageBuilder,
                    outcomeMessageBuilder,
                    onFailure,
                    onSuccess,
                    shouldAttemptInventoryFallback,
                    inventoryFallback,
                    fallbackExecutor);
            }

            public string ExposedSkillName => SkillName;

            public string ExposedLogPrefix => LogPrefix;
        }

        [Test]
        public void Constructor_PreservesSkillNameAndLogPrefix()
        {
            var service = new TestableGatheringService("Mining", "[TestPrefix]");

            Assert.AreEqual("Mining", service.ExposedSkillName);
            Assert.AreEqual("[TestPrefix]", service.ExposedLogPrefix);
        }

        [Test]
        public void Constructor_DefaultsLogPrefixWhenNull()
        {
            var service = new TestableGatheringService("Fishing", null);

            Assert.AreEqual("[Companion]", service.ExposedLogPrefix);
        }

        [Test]
        public void CreateSingleTargetRequest_PopulatesAllDelegates()
        {
            var service = new TestableGatheringService("Woodcutting", "[Logs]");

            CompanionSkillCommandGuard.CooldownCheck<DummyResult> cooldownCheck = (
                CompanionSkillCooldownTracker tracker,
                out DummyResult result) =>
            {
                result = DummyResult.Cooldown;
                return false;
            };
            CompanionSkillCommandGuard.SingleCommandExecutor<DummyController, DummyResult> executor = (
                DummyController controller,
                out DummyResult result) =>
            {
                result = DummyResult.Success;
                return true;
            };
            Func<DummyResult, bool> treatResultAsSuccess = r => r == DummyResult.Failure;
            Func<DummyResult, string> cooldownBuilder = r => r.ToString();
            Func<bool, DummyResult, string> outcomeBuilder = (accepted, r) => accepted ? "Accepted" : r.ToString();
            Action<DummyResult> onFailure = _ => { };
            Action<DummyResult> onSuccess = _ => { };
            Func<DummyResult, bool> shouldAttemptFallback = _ => true;
            Func<bool> inventoryFallback = () => true;
            CompanionSkillCommandGuard.SingleCommandExecutor<DummyController, DummyResult> fallbackExecutor = (
                DummyController controller,
                out DummyResult result) =>
            {
                result = DummyResult.Success;
                return true;
            };
            Action<DummyResult> resultObserver = _ => { };

            var request = service.BuildSingleTargetRequest(
                cooldownCheck,
                executor,
                treatResultAsSuccess,
                cooldownBuilder,
                outcomeBuilder,
                onFailure,
                onSuccess,
                shouldAttemptFallback,
                inventoryFallback,
                fallbackExecutor,
                resultObserver);

            Assert.AreEqual("Woodcutting", request.SkillName);
            Assert.AreEqual("[Logs]", request.LogPrefix);
            Assert.AreSame(cooldownCheck, request.CooldownCheck);
            Assert.AreSame(executor, request.CommandExecutor);
            Assert.AreSame(treatResultAsSuccess, request.TreatResultAsSuccess);
            Assert.AreSame(cooldownBuilder, request.CooldownMessageBuilder);
            Assert.AreSame(outcomeBuilder, request.OutcomeMessageBuilder);
            Assert.AreSame(onFailure, request.OnFailure);
            Assert.AreSame(onSuccess, request.OnSuccess);
            Assert.AreSame(shouldAttemptFallback, request.ShouldAttemptInventoryFallback);
            Assert.AreSame(inventoryFallback, request.InventoryFallback);
            Assert.AreSame(fallbackExecutor, request.FallbackExecutor);
            Assert.AreSame(resultObserver, request.ResultObserver);
        }

        [Test]
        public void CreateSingleTargetRequest_ThrowsWhenExecutorMissing()
        {
            var service = new TestableGatheringService("Mining");

            Assert.Throws<ArgumentNullException>(() =>
                service.BuildSingleTargetRequest(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));
        }

        [Test]
        public void CreateAreaRequest_PopulatesAllDelegates()
        {
            var service = new TestableGatheringService("Fishing", "[Area]");

            CompanionSkillCommandGuard.CooldownCheck<DummyResult> cooldownCheck = (
                CompanionSkillCooldownTracker tracker,
                out DummyResult result) =>
            {
                result = DummyResult.Cooldown;
                return true;
            };
            CompanionSkillCommandGuard.AreaCommandExecutor<DummyController, DummyResult> executor = (
                DummyController controller,
                float radius,
                out DummyResult result) =>
            {
                result = DummyResult.Success;
                return true;
            };
            Func<DummyResult, bool> treatFailureAsSuccess = r => r == DummyResult.Cooldown;
            Func<float, DummyResult, string> cooldownBuilder = (radius, r) => $"{radius}:{r}";
            Func<bool, DummyResult, float, string> outcomeBuilder = (accepted, r, radius) => accepted ? "Accepted" : radius.ToString();
            Action<DummyResult> onFailure = _ => { };
            Action<DummyResult> onSuccess = _ => { };
            Func<DummyResult, bool> shouldAttemptFallback = _ => true;
            Func<bool> inventoryFallback = () => false;
            CompanionSkillCommandGuard.AreaCommandExecutor<DummyController, DummyResult> fallbackExecutor = (
                DummyController controller,
                float radius,
                out DummyResult result) =>
            {
                result = DummyResult.Success;
                return true;
            };

            var request = service.BuildAreaRequest(
                DummyResult.Failure,
                cooldownCheck,
                executor,
                treatFailureAsSuccess,
                cooldownBuilder,
                outcomeBuilder,
                onFailure,
                onSuccess,
                shouldAttemptFallback,
                inventoryFallback,
                fallbackExecutor);

            Assert.AreEqual("Fishing", request.SkillName);
            Assert.AreEqual("[Area]", request.LogPrefix);
            Assert.AreEqual(DummyResult.Failure, request.GuardRejectionResult);
            Assert.AreSame(cooldownCheck, request.CooldownCheck);
            Assert.AreSame(executor, request.CommandExecutor);
            Assert.AreSame(treatFailureAsSuccess, request.TreatFailureAsSuccess);
            Assert.AreSame(cooldownBuilder, request.CooldownMessageBuilder);
            Assert.AreSame(outcomeBuilder, request.OutcomeMessageBuilder);
            Assert.AreSame(onFailure, request.OnFailure);
            Assert.AreSame(onSuccess, request.OnSuccess);
            Assert.AreSame(shouldAttemptFallback, request.ShouldAttemptInventoryFallback);
            Assert.AreSame(inventoryFallback, request.InventoryFallback);
            Assert.AreSame(fallbackExecutor, request.FallbackExecutor);
        }

        [Test]
        public void CreateAreaRequest_ThrowsWhenExecutorMissing()
        {
            var service = new TestableGatheringService("Woodcutting");

            Assert.Throws<ArgumentNullException>(() =>
                service.BuildAreaRequest(
                    DummyResult.Failure,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));
        }

        [Test]
        public void Constructor_ThrowsWhenSkillNameMissing()
        {
            Assert.Throws<ArgumentException>(() => new TestableGatheringService(""));
        }
    }
}
