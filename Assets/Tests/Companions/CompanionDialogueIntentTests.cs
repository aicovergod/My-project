using System.Linq;
using Companions.Conversation;
using NUnit.Framework;

namespace Tests.Companions
{
    public class CompanionDialogueIntentTests
    {
        private CompanionDialogueParser parser;

        [SetUp]
        public void SetUp()
        {
            var rules = CompanionDialoguePatterns.CreateDefaultProfile();
            parser = new CompanionDialogueParser(rules);
        }

        [Test]
        public void ParserDetectsAcceptSkillPlanIntent()
        {
            var result = parser.Parse("yeah let's mine more");
            Assert.IsTrue(result.Matches.Any(m => m.Intent == CompanionDialogueIntent.AcceptSkillPlan),
                "Expected AcceptSkillPlan intent for affirmative skill phrase.");
        }

        [Test]
        public void ParserDetectsDeclineSkillPlanIntent()
        {
            var result = parser.Parse("nah let's not fish right now");
            Assert.IsTrue(result.Matches.Any(m => m.Intent == CompanionDialogueIntent.DeclineSkillPlan),
                "Expected DeclineSkillPlan intent for rejection phrase.");
        }

        [Test]
        public void ParserDetectsDeferSkillPlanIntent()
        {
            var result = parser.Parse("maybe later for cooking");
            Assert.IsTrue(result.Matches.Any(m => m.Intent == CompanionDialogueIntent.DeferSkillPlan),
                "Expected DeferSkillPlan intent for deferment phrase.");
        }

        [Test]
        public void ParserDetectsRequestAlternateSkillIntent()
        {
            var result = parser.Parse("surprise me with another skill");
            Assert.IsTrue(result.Matches.Any(m => m.Intent == CompanionDialogueIntent.RequestAlternateSkill),
                "Expected RequestAlternateSkill intent for alternate request.");
        }

        [Test]
        public void GreetingDoesNotRegisterAsComplimentWhenUsingTimeOfDay()
        {
            var result = parser.Parse("good morning isla im back");
            Assert.IsTrue(result.Matches.Any(m => m.Intent == CompanionDialogueIntent.Greeting),
                "Expected greeting intent when using a time-of-day salutation.");
            Assert.IsFalse(result.Matches.Any(m => m.Intent == CompanionDialogueIntent.Compliment),
                "Time-of-day greeting should not register as a compliment.");
        }

        [Test]
        public void ParserStillDetectsComplimentAfterPraiseBucketSplit()
        {
            var result = parser.Parse("awesome job helping me");
            Assert.IsTrue(result.Matches.Any(m => m.Intent == CompanionDialogueIntent.Compliment),
                "Expected compliment intent for a strong praise phrase.");
        }

        [Test]
        public void ResponseCatalogIncludesProactiveSkillTemplates()
        {
            CompanionResponseCatalog.EnsureDefaults();
            var proactive = CompanionResponseCatalog.GetTemplates(CompanionDialogueIntent.ProactiveSkillQuestion);
            Assert.IsTrue(proactive.Any(), "Expected proactive skill question templates to be registered.");
            Assert.IsTrue(proactive.Any(t => t.Text.Contains("{suggestedSkill}")),
                "Proactive templates should reference the suggested skill placeholder.");
        }

        [Test]
        public void ResponseCatalogIncludesSkillFollowUpTemplates()
        {
            CompanionResponseCatalog.EnsureDefaults();

            static void AssertTemplatesPresent(CompanionDialogueIntent intent)
            {
                var templates = CompanionResponseCatalog.GetTemplates(intent);
                Assert.IsNotNull(templates, $"Expected template collection for {intent}.");
                Assert.IsTrue(templates.Any(), $"Expected at least one template registered for {intent}.");
            }

            AssertTemplatesPresent(CompanionDialogueIntent.AcceptSkillPlan);
            AssertTemplatesPresent(CompanionDialogueIntent.DeclineSkillPlan);
            AssertTemplatesPresent(CompanionDialogueIntent.DeferSkillPlan);
            AssertTemplatesPresent(CompanionDialogueIntent.RequestAlternateSkill);
        }
    }
}
