namespace Em.Shared
{
   /// <summary>
   /// Envelope of the result of one action on the <c>EmApp</c> dispatcher. Every API response is wrapped
   /// in this shape and serialized as JSON, for both success and error, so the response shape is always
   /// consistent across every action of every module.
   /// </summary>
   public class ActionResult
   {
      /// <summary>
      /// Indicates whether <see cref="Data"/> holds a valid value to read.
      /// </summary>
      public bool HasData { get; set; }

      /// <summary>
      /// Return value of the invoked action. May be <c>null</c> when the action returns no data or failed
      /// to run.
      /// </summary>
      public object? Data { get; set; }

      /// <summary>
      /// <c>true</c> when the action ran without errors; <c>false</c> when it failed (e.g. parameter
      /// binding failed, the action was not found, or an exception was thrown during invocation).
      /// </summary>
      public bool ValidResult { get; set; }

      /// <summary>
      /// Short error message when <see cref="ValidResult"/> is <c>false</c>.
      /// </summary>
      public string? ErrorMessage { get; set; }

      /// <summary>
      /// Additional messages about the execution, apart from the main error message.
      /// </summary>
      public string? Messages { get; set; }

      /// <summary>
      /// Exception stack trace, when an unhandled error occurred while running the action.
      /// </summary>
      public string? ErrorStackTrace { get; set; }

      /// <summary>
      /// Full name (assembly-qualified or full name) of the type of <see cref="Data"/>, used by the client
      /// to know how to deserialize <see cref="Data"/> correctly.
      /// </summary>
      public string ResultTypeFullName { get; set; } = "";

      /// <summary>
      /// HTTP status code representing the result (e.g. 200, 400, 404, 500).
      /// </summary>
      public int StatusCode { get; set; }

      /// <summary>
      /// Full name of the service type (class) handling this action, for diagnostics.
      /// </summary>
      public string ServiceType { get; set; } = "";

      /// <summary>
      /// Name of the action/method invoked on that service, for diagnostics.
      /// </summary>
      public string ServiceAction { get; set; } = "";
   }
}
