using System.Reflection;

namespace Em.Api.Core
{
   /// <summary>Describes one action discovered on a module service: its HTTP method, module, name, and method.</summary>
   public class ActionDefinition
   {
      /// <summary>The HTTP method the action answers to.</summary>
      public Em.Shared.HttpMethod HttpMethod { get; set; }
      /// <summary>Name of the module that owns the action.</summary>
      public string Module { get; set; } = "";
      /// <summary>Name of the action, as it appears in the route.</summary>
      public string ActionName { get; set; } = "";
      /// <summary>The method that implements the action.</summary>
      public MethodInfo MethodInfo { get; set; } = null!;
      /// <summary>The service type that declares the action.</summary>
      public Type Type { get; set; } = null!;

      /// <summary>
      /// Taken from <c>[GetAction]</c>/<c>[PostAction]</c>: true when the action may be accessed without
      /// authentication. Enforced by <c>EmApp.ProcessRequest</c> - a non-public action is answered 401 when
      /// the request carries no proven identity.
      /// </summary>
      public bool IsPublicAction { get; set; }

      /// <summary>
      /// Taken from <c>[GetAction(claim: ...)]</c>/<c>[PostAction(claim: ...)]</c>. <c>null</c> means this
      /// action needs any claim of <see cref="Module"/> - it does not mean unconditional. When set, this
      /// action needs exactly that claim in the same module. Enforced by <c>EmApp.ProcessRequest</c> after
      /// <see cref="IsPublicAction"/> is checked.
      /// </summary>
      public string? RequiredClaim { get; set; }

      /// <summary>
      /// <c>false</c> for services the engine registers itself before any module callback runs, the only
      /// ones outside the per-action claim check - the reason is in the internal overload
      /// <c>EmAppBuilder.AddService&lt;T1, T2&gt;(bool)</c>. Always <c>true</c> for module actions, including
      /// modules that have no registered claim at all: those are refused, not let through, so a module that
      /// forgot to be given claims is noisy instead of silently open.
      /// <para>
      /// <see cref="RequiredClaim"/> is still enforced even when this is <c>false</c>. Only the default
      /// requirement "holds any claim in this module" is lifted.
      /// </para>
      /// </summary>
      public bool EnforcesClaims { get; set; }

      /// <summary>
      /// Time limit specific to this action, taken as-is from
      /// <c>[GetAction(requestTimeoutSecond: ...)]</c>. <c>null</c> - which applies to almost every action -
      /// means this action follows <c>EmAppBuilder.HttpRequestTimeout</c>.
      /// </summary>
      /// <remarks>
      /// Deliberately stored raw, not already combined with the application's time limit: the combining
      /// happens per request in <c>EmApp.ProcessRequest</c>. If it were combined here, the result would
      /// depend on the order written in <c>Program.cs</c> - the application's time limit and the module
      /// registration are filled in the same callback - and a swapped order would make nobody complain.
      /// <para>
      /// Always <c>null</c> for <c>POST</c> actions: the engine never cuts off write actions, so
      /// <c>[PostAction]</c> has no way to state a time limit.
      /// </para>
      /// </remarks>
      public TimeSpan? RequestTimeout { get; set; }

      /// <summary>
      /// Position of the <see cref="Stream"/> parameter in the action signature, or <c>null</c> when this
      /// action is not a streaming action. When set, the request body is not read as JSON: it is handed
      /// as-is to the parameter at this position, and the other parameter (if any) is taken from the
      /// <see cref="Defaults.StreamPayloadHeader"/> header.
      /// </summary>
      public int? StreamParameterIndex { get; set; }

      /// <summary>
      /// Position of the one and only parameter other than the stream on a streaming action - the object
      /// filled from the <see cref="Defaults.StreamPayloadHeader"/> header. <c>null</c> when the action only
      /// accepts a stream, or when this action is not a streaming action.
      /// </summary>
      public int? StreamPayloadParameterIndex { get; set; }

      /// <summary>
      /// <c>true</c> when this action returns <c>Task&lt;Stream&gt;</c>. The answer is then not a JSON
      /// envelope, but the content of that stream as-is as <c>application/octet-stream</c>; only failures
      /// are still answered with a JSON envelope.
      /// </summary>
      public bool ReturnsStream { get; set; }
   }
}