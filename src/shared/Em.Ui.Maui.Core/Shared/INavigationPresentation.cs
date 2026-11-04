namespace Em.Ui.Maui.Shared;

/// <summary>Optionally supplies entry-local shell presentation without changing navigation identity or access.</summary>
public interface INavigationPresentation
{
   /// <summary>The stable presentation object owned by this body for its lifetime.</summary>
   NavigationPresentation Presentation { get; }
}
