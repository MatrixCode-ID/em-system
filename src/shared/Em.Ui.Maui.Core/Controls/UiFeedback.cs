namespace Em.Ui.Maui.Controls;

/// <summary>The meaning of a presentation message, independent of its color.</summary>
public enum FeedbackKind { Info, Success, Partial, Error, Stale }

/// <summary>A business label and its outcome. Internal identifiers should not be supplied as labels.</summary>
public sealed record FeedbackItem(string Label, FeedbackKind Kind, string Message)
{
   /// <summary>The outcome in words, also readable without color.</summary>
   public string Display => $"{Label} · {Kind}: {Message}";
}

/// <summary>A retained result snapshot. Refreshing the list must not overwrite this snapshot.</summary>
public sealed record BatchFeedback(IReadOnlyList<FeedbackItem> Items)
{
   /// <summary>The combined presentation outcome.</summary>
   public FeedbackKind Kind => Items.Count == 0 ? FeedbackKind.Info :
      Items.All(x => x.Kind == FeedbackKind.Success) ? FeedbackKind.Success :
      Items.All(x => x.Kind == FeedbackKind.Error) ? FeedbackKind.Error : FeedbackKind.Partial;
   /// <summary>Counts by outcome, without implying a final server count before completion.</summary>
   public string Summary => $"{Items.Count(x => x.Kind == FeedbackKind.Success)} succeeded · " +
      $"{Items.Count(x => x.Kind == FeedbackKind.Error)} failed · " +
      $"{Items.Count(x => x.Kind is not (FeedbackKind.Success or FeedbackKind.Error))} skipped/review";
}
