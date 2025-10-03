using NUnit.Framework;
using Skills.Common;

public class TickProgressTrackerTests
{
    [Test]
    public void AdvanceCompletesAfterRequiredTicks()
    {
        var tracker = new TickProgressTracker();
        tracker.Reset(3);

        Assert.IsFalse(tracker.Advance(), "First tick should not complete the action.");
        Assert.AreEqual(1, tracker.ProgressTicks);
        Assert.IsFalse(tracker.Advance(), "Second tick should not complete the action.");
        Assert.AreEqual(2, tracker.ProgressTicks);
        Assert.IsTrue(tracker.Advance(), "Third tick should complete the action.");
        Assert.AreEqual(3, tracker.ProgressTicks);
    }

    [Test]
    public void ResetMidActionOverridesProgress()
    {
        var tracker = new TickProgressTracker();
        tracker.Reset(4);
        tracker.Advance();

        tracker.Reset(2);

        Assert.AreEqual(0, tracker.ProgressTicks, "Progress should reset when requirement changes mid-action.");
        Assert.AreEqual(2, tracker.RequiredTicks, "New requirement should be cached.");
        Assert.IsFalse(tracker.Advance(), "First tick after reset should not complete the action.");
        Assert.IsTrue(tracker.Advance(), "Second tick after reset should complete the action.");
    }

    [Test]
    public void EventsFireInSequence()
    {
        var tracker = new TickProgressTracker();

        int resets = 0;
        int advancedCount = 0;
        int completedCount = 0;
        (int progress, int required) lastAdvance = default;
        (int progress, int required) lastComplete = default;

        tracker.ProgressReset += required =>
        {
            resets++;
        };

        tracker.TickAdvanced += (progress, required) =>
        {
            advancedCount++;
            lastAdvance = (progress, required);
        };

        tracker.TickCompleted += (progress, required) =>
        {
            completedCount++;
            lastComplete = (progress, required);
        };

        tracker.Reset(2);

        tracker.Advance();
        tracker.Advance();

        Assert.AreEqual(1, resets, "Reset should fire exactly once for the initial setup.");
        Assert.AreEqual(2, advancedCount, "TickAdvanced should fire on every increment.");
        Assert.AreEqual((2, 2), lastAdvance);
        Assert.AreEqual(1, completedCount, "Completion event should fire exactly once.");
        Assert.AreEqual((2, 2), lastComplete);
    }

    [Test]
    public void ClearProgressMaintainsRequirement()
    {
        var tracker = new TickProgressTracker();
        tracker.Reset(3);
        tracker.Advance();

        tracker.ClearProgress();

        Assert.AreEqual(0, tracker.ProgressTicks, "ClearProgress should reset tick progress.");
        Assert.AreEqual(3, tracker.RequiredTicks, "Requirement should remain unchanged when clearing progress.");
        Assert.IsFalse(tracker.Advance());
        Assert.AreEqual(1, tracker.ProgressTicks);
    }
}
