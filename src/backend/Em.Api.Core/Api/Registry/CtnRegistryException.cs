namespace Em.Api.Core.Registry
{
   /// <summary>
   /// A failure on the <c>/v2</c> path together with its OCI error code (<c>NAME_UNKNOWN</c>,
   /// <c>DENIED</c>, ...). Unlike <c>ActionException</c>: its answer has the shape <c>{"errors":[...]}</c>
   /// understood by the Docker client, not the action envelope.
   /// </summary>
   internal sealed class CtnRegistryException(int statusCode, string code, string message, object? detail = null)
      : Exception(message)
   {
      public int StatusCode { get; } = statusCode;
      public string Code { get; } = code;
      public object? Detail { get; } = detail;
   }
}
