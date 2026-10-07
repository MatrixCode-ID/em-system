namespace Em.Shared
{
   /// <summary>
   /// Marks a service method (or an implemented interface method) as an action reachable over HTTP
   /// <c>POST</c> through the <c>EmApp</c> dispatcher. Method parameters are bound from the request body,
   /// a JSON array of <see cref="PostMethodPayload"/>. A method with this attribute must return
   /// <c>Task</c> or <c>Task&lt;T&gt;</c>.
   /// </summary>
   /// <param name="action">
   /// Optional action name overriding the method name as the route identifier. When empty, the method
   /// name itself is the action name.
   /// </param>
   /// <param name="claim">
   /// Claim name required by this action, without the module name - see <see cref="Claim"/>.
   /// </param>
   [AttributeUsage(AttributeTargets.Method)]
   public class PostActionAttribute(string action = "", string? claim = null) : Attribute
   {
      /// <summary>
      /// Overridden action name, or an empty string when the method name is used by default.
      /// </summary>
      public string Action { get; } = action;

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
      /// <c>[PostAction(claim: "ApprovePricing")]</c>.
      /// </para>
      /// <para>
      /// Administrators and the debug token path skip this check entirely, as on the UI side - so the value
      /// here can never lock either of them out.
      /// </para>
      /// </remarks>
      public string? Claim { get; } = claim;

      /// <summary>
      /// <c>true</c> when this action may be called without authentication. Defaults to <c>false</c>, so
      /// actions are closed unless explicitly marked - a new action that forgot the annotation is closed,
      /// not open. Written as a property (not a constructor parameter) so it reads at the call site:
      /// <c>[PostAction(IsPublicAction = true)]</c>.
      /// </summary>
      /// <remarks>
      /// This is the only marker that really means "unrestricted": an action carrying it is served even
      /// without any identity, so <see cref="Claim"/> is never checked for it.
      /// </remarks>
      public bool IsPublicAction { get; set; }
   }
}
