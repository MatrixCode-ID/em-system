using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Core
{
   public partial class EmApp
   {
      /// <summary>Whether a debug build is testing the ordinary sign-in flow.</summary>
      public bool IsSimulatingLogin { get; private set; }

      /// <summary>Whether debug tools are active outside the simulated sign-in flow.</summary>
      public bool IsDebugActive => IsDebugMode && !IsSimulatingLogin;

      private ApiConnection? _simulationDebugConnection;
      private bool _simulationTransition;

      private void ApplyDebugToken(string? token) {
         foreach (var connection in DebugConnections.Concat(UIConnections).Concat(_apiClients.Keys).Distinct())
            connection.DebugToken = token;
         if (ActiveConnection is { } active) active.DebugToken = token;
      }

      internal async Task<bool> BeginLoginSimulationAsync() {
         if (!IsDebugMode)
            throw new InvalidOperationException("Simulate Login is only available in debug mode.");
         if (IsSimulatingLogin || _simulationTransition) return false;
         _simulationTransition = true;
         try {
            if (!await MainStack.AskCurrentToLeave()) return false;
            _simulationDebugConnection = ActiveConnection;
            IsSimulatingLogin = true;
            if (_sessionClient is not null)
               await EndSessionCoreAsync(false, null, raiseEnded: false);
            await MainStack.ReleaseAll();
            ApplyDebugToken(null);
            SetActiveUser(null);
            await ShowLoginScreen(null);
            return true;
         }
         finally { _simulationTransition = false; }
      }

      internal async Task<bool> EndLoginSimulationAsync() {
         if (_simulationTransition) return false;
         if (!IsSimulatingLogin) return true;
         _simulationTransition = true;
         try {
            if (!await MainStack.AskCurrentToLeave()) return false;
            if (_sessionClient is not null)
               await EndSessionCoreAsync(true, null, raiseEnded: false);
            await MainStack.ReleaseAll();
            IsSimulatingLogin = false;
            ApplyDebugToken(_debugToken);
            SetActiveUser(CreateDebuggerUser(this));
            ActiveConnection = _simulationDebugConnection ?? DefaultDebugConnection;
            _simulationDebugConnection = null;
            _claimsRefresh = RefreshClaimsAsync();
            await GoHomeAsync();
            return true;
         }
         finally { _simulationTransition = false; }
      }
   }
}
