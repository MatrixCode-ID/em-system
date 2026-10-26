using Em.Ui.Maui.Controls;

namespace Em.Ui.Core.Tests;

public class MauiFeedbackTests
{
   [Fact]
   public void MixedOutcomesRetainBusinessLabelsAndCounts() {
      var result = new BatchFeedback([
         new("Document A", FeedbackKind.Success, "Signed"),
         new("Document B", FeedbackKind.Error, "Access changed"),
         new("Document C", FeedbackKind.Info, "No longer pending")]);
      Assert.Equal(FeedbackKind.Partial, result.Kind);
      Assert.Equal("1 succeeded · 1 failed · 1 skipped/review", result.Summary);
      Assert.Contains("Document B", result.Items[1].Display);
      Assert.Contains("Error", result.Items[1].Display);
   }

   [Theory]
   [InlineData(FeedbackKind.Success, FeedbackKind.Success)]
   [InlineData(FeedbackKind.Error, FeedbackKind.Error)]
   [InlineData(FeedbackKind.Stale, FeedbackKind.Partial)]
   public void CompletedOutcomeIsSeparateFromListFreshness(FeedbackKind item, FeedbackKind expected) {
      var result = new BatchFeedback([new("Business record", item, "Outcome")]);
      Assert.Equal(expected, result.Kind);
      Assert.Single(result.Items);
   }

   [Fact]
   public void EmptyResultDoesNotClaimSuccess() => Assert.Equal(FeedbackKind.Info, new BatchFeedback([]).Kind);
}
