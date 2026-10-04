namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Kegagalan jalur <c>/v2</c> beserta kode kesalahan OCI-nya (<c>NAME_UNKNOWN</c>, <c>DENIED</c>, ...).
   /// Beda dari <c>ActionException</c>: jawabannya berbentuk <c>{"errors":[...]}</c> yang dimengerti
   /// klien Docker, bukan envelope action.
   /// </summary>
   internal sealed class CtnRegistryException(int statusCode, string code, string message, object? detail = null)
      : Exception(message)
   {
      public int StatusCode { get; } = statusCode;
      public string Code { get; } = code;
      public object? Detail { get; } = detail;
   }
}
