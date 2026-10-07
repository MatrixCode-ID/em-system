namespace Em.Shared
{
   /// <summary>
   /// Marks a service method (or an implemented interface method) as an action reachable over HTTP
   /// <c>GET</c> through the <c>EmApp</c> dispatcher. A method with this attribute must return
   /// <c>Task</c> or <c>Task&lt;T&gt;</c>.
   /// </summary>
   /// <param name="action">
   /// Optional action name overriding the method name as the route identifier. When <c>null</c>, the
   /// method name itself is the action name.
   /// </param>
   /// <param name="claim">
   /// Claim name required by this action, without the module name - see <see cref="Claim"/>.
   /// </param>
   [AttributeUsage(AttributeTargets.Method)]
   public class GetActionAttribute(string? action = null, string? claim = null) : Attribute
   {
      /// <summary>
      /// Marks a <c>GET</c> action whose timeout differs from the application-wide timeout. Use it only when
      /// needed - actions that do not state one follow <c>EmAppBuilder.HttpRequestTimeout</c>, which applies
      /// to almost all of them.
      /// </summary>
      /// <param name="requestTimeoutSecond">
      /// Working time limit of this action in seconds. A positive number replaces the application timeout;
      /// a negative number means this action has no time limit at all.
      /// </param>
      /// <param name="action">
      /// Optional action name overriding the method name as the route identifier, as in the constructor
      /// without a timeout.
      /// </param>
      /// <param name="claim">
      /// Claim name required by this action, as in the constructor without a timeout - see
      /// <see cref="Claim"/>.
      /// </param>
      /// <remarks>
      /// Written as a constructor overload, not as a property like <see cref="IsPublicAction"/>, because
      /// attribute arguments may only be constants - neither <c>TimeSpan</c> nor <c>int?</c> is valid
      /// there. With an overload the restriction only affects the parameter, so
      /// <see cref="RequestTimeout"/> can really be <c>TimeSpan?</c> and "not stated" is simply
      /// <c>null</c> - with no magic number for readers to memorize.
      /// <para>
      /// Keep in mind when extending it: the timeout here only states how long the server is willing to
      /// work, not how long the client is willing to wait. The client has its own timeout, and if that is
      /// not raised too, all that grows is how long the server works on an answer nobody waits for.
      /// </para>
      /// </remarks>
      /// <exception cref="ArgumentOutOfRangeException">
      /// Thrown when <paramref name="requestTimeoutSecond"/> is zero - a zero-second timeout cancels the
      /// action before it can run, so it is almost certainly a typo and it is better to be loud than
      /// silent.
      /// </exception>
      public GetActionAttribute(int requestTimeoutSecond, string? action = null, string? claim = null)
         : this(action, claim) {
         if (requestTimeoutSecond == 0) {
            throw new ArgumentOutOfRangeException(nameof(requestTimeoutSecond),
               "A request timeout of zero seconds would cancel the action before it starts. Pass a positive number of seconds, a negative number for no limit at all, or omit the argument to follow the application-wide timeout.");
         }

         RequestTimeout = requestTimeoutSecond < 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(requestTimeoutSecond);
      }

      /// <summary>
      /// Overridden action name, or <c>null</c> when the method name is used by default.
      /// </summary>
      public string? Action { get; } = action;

      /// <summary>
      /// Claim name required by this action, written without the module name - the module is taken from
      /// where this action is registered, so it need not (and must not) be repeated here.
      /// </summary>
      /// <remarks>
      /// <c>null</c> - which applies to almost every action - does <b>not</b> mean "unrestricted". It
      /// means the caller needs <i>any</i> claim of this action's module; a caller without a single claim in
      /// that module is still rejected. Only <see cref="IsPublicAction"/> means "unrestricted", and that is
      /// a different property.
      /// <para>
      /// When set, the caller must hold exactly that claim in the same module - holding another claim of
      /// the same module is not enough. Used for actions that need a narrower right than "may enter this
      /// module":
      /// <c>[GetAction(claim: "ViewPricing")]</c>.
      /// </para>
      /// <para>
      /// Administrators and the debug token path skip this check entirely, as on the UI side - so the value
      /// here can never lock either of them out.
      /// </para>
      /// </remarks>
      public string? Claim { get; } = claim;

      /// <summary>
      /// Specific timeout of this action, or <c>null</c> when the action does not state its own timeout and
      /// therefore follows the application-wide one. <c>Timeout.InfiniteTimeSpan</c> means the action is
      /// deliberately left without a limit.
      /// </summary>
      /// <remarks>
      /// Can only be set through the constructor overload taking <c>requestTimeoutSecond</c> - there is no
      /// way to set it as a named argument, because <c>TimeSpan</c> is not a valid attribute argument type.
      /// </remarks>
      public TimeSpan? RequestTimeout { get; }

      /// <summary>
      /// <c>true</c> when this action may be called without authentication. Defaults to <c>false</c>, so
      /// actions are closed unless explicitly marked - a new action that forgot the annotation is closed,
      /// not open. Written as a property (not a constructor parameter) so it reads at the call site:
      /// <c>[GetAction(IsPublicAction = true)]</c>.
      /// </summary>
      /// <remarks>
      /// This is the only marker that really means "unrestricted": an action carrying it is served even
      /// without any identity, so <see cref="Claim"/> is never checked for it.
      /// </remarks>
      public bool IsPublicAction { get; set; }
   }
}
