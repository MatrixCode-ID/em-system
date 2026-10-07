using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Model of one saved API connection profile (host, timeout, etc.), stored in the Registry through
   /// <c>EmApp</c>.
   /// </summary>
   public class ApiConnection : NotifyPropertyBase
   {
      /// <summary>
      /// Name of the connection profile, used as the unique identifier among connections.
      /// </summary>
      public string ProfileName {
         get => Get<string>();
         set => Set(value);
      }

      /// <summary>
      /// Address/host of the target API server.
      /// </summary>
      public string Host {
         get => Get<string>();
         set => Set(value);
      }

      /// <summary>
      /// Connection timeout in seconds.
      /// </summary>
      public int Timeout {
         get => Get<int>();
         set => Set(value);
      }

      /// <summary>
      /// When <c>true</c>, SSL/TLS certificate validation errors are ignored when connecting to this server.
      /// </summary>
      public bool IgnoreSslErrors {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// <c>true</c> when this profile comes from a debug configuration written in code
      /// (<c>DebugBuilder</c>), not from the Registry. A debug connection is only shown in the UI list; it
      /// must not be saved, changed, or deleted through the connection dialog.
      /// </summary>
      public bool IsDebugConnection {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// The debug token sent on every request through this connection, or <c>null</c> when the connection is
      /// not a debug connection. What is stored here is the finished token - the signature made once when the
      /// application starts - not its key, so the request path does not touch cryptography at all.
      /// </summary>
      /// <remarks>
      /// This property is never saved to the Registry: connection storage writes its fields one by one, and
      /// debug connections are already refused entry to the Registry from the start.
      /// </remarks>
      public string? DebugToken {
         get => Get<string?>();
         set => Set(value);
      }

      /// <summary>
      /// Creates an API client for this profile, complete with a handler that already follows
      /// <see cref="IgnoreSslErrors"/>. The handler is created per connection, not shared, because the choice
      /// to turn off certificate validation applies to one server only - and because <see cref="ApiClient"/>
      /// releases its handler itself when disposed.
      /// </summary>
      public ApiClient CreateApiClient() =>
         ApiClient.Create(this, Defaults.CreateHttpClientHandler(IgnoreSslErrors));
   }
}