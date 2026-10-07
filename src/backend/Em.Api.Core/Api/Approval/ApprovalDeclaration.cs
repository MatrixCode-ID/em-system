using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Position of a box on the document PDF, in millimeters from the top-left corner of the page.
   /// </summary>
   /// <param name="X">Distance from the left edge.</param>
   /// <param name="Y">Distance from the top edge.</param>
   /// <param name="Width">Width of the box.</param>
   /// <param name="Height">Height of the box.</param>
   /// <param name="Page">Page this box is on, starting from one.</param>
   /// <remarks>
   /// The position is fixed and written by the developer of the module that owns the document, because
   /// they know the document's design. The size is frozen into the request at submission, so a design
   /// change only applies to requests submitted afterwards. To measure it, use the calibration action
   /// that draws these boxes over the real document.
   /// </remarks>
   public record ApprovalSlot(double X, double Y, double Width = 60, double Height = 18, int Page = 1)
   {
      /// <summary>
      /// Creates a box position. Same as its constructor, written as a method so a flow declaration full of
      /// boxes still reads as a list of positions, not a list of <c>new</c>.
      /// </summary>
      /// <param name="x">Distance from the left edge.</param>
      /// <param name="y">Distance from the top edge.</param>
      /// <param name="width">Width of the box.</param>
      /// <param name="height">Height of the box.</param>
      /// <param name="page">Page this box is on, starting from one.</param>
      public static ApprovalSlot At(double x, double y, double width = 60, double height = 18, int page = 1) =>
         new(x, y, width, height, page);
   }

   /// <summary>
   /// Marks a property of the key record as part of the document key, together with its order.
   /// </summary>
   /// <param name="order">
   /// Order of this part within its key. That order determines the canonical form of the key, so changing
   /// it after requests exist changes the meaning of keys that are already stored.
   /// </param>
   /// <remarks>
   /// Legacy document keys generally consist of several parts. The module declares them as a typed record
   /// and receives that record in its handlers; the engine translates it to the canonical form and back,
   /// so no module code splits the key string itself.
   /// </remarks>
   [AttributeUsage(AttributeTargets.Property)]
   public class KeyPartAttribute(int order) : Attribute
   {
      /// <summary>Order of this part within its key.</summary>
      public int Order { get; } = order;
   }

   /// <summary>
   /// Result of the module's check on a step: it may be decided, or it is blocked together with the reason.
   /// </summary>
   /// <remarks>
   /// A block here is not a rejection. The request keeps waiting at that step, and the check is re-run
   /// every time the screen is opened, so as soon as the condition is met the step opens by itself. A block
   /// that truly obstructs work can be overridden by holders of a claim the module chooses itself, and a
   /// signature produced by an override is specially marked because it is an emergency.
   /// </remarks>
   public class ApprovalGuard
   {
      private ApprovalGuard() { }

      /// <summary>This step may be decided.</summary>
      public static ApprovalGuard Allow { get; } = new() { IsAllowed = true };

      /// <summary>
      /// This step may not be decided yet.
      /// </summary>
      /// <param name="reason">
      /// The reason, shown next to the disabled button. Written to be read by the signer, so state what has
      /// to happen for it to open.
      /// </param>
      /// <param name="overridableBy">
      /// Claim that may override this block, written without its module name. Empty means the block cannot be
      /// overridden by anyone and can only disappear when its condition is met.
      /// </param>
      public static ApprovalGuard Block(string reason, string? overridableBy = null) =>
         new() { IsAllowed = false, Reason = reason, OverridableBy = overridableBy };

      /// <summary>This step may be decided.</summary>
      public bool IsAllowed { get; private init; }

      /// <summary>The reason for the block.</summary>
      public string? Reason { get; private init; }

      /// <summary>Claim that may override this block.</summary>
      public string? OverridableBy { get; private init; }
   }

   /// <summary>
   /// Info available when the engine asks the module something about a request.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   public interface IApprovalContext<out TServices, out TKey>
   {
      /// <summary>The service of the module that owns the document, ready to use.</summary>
      TServices Services { get; }

      /// <summary>The key of the document being processed, already in typed form.</summary>
      TKey DocKey { get; }

      /// <summary>Version of the document being processed.</summary>
      string DocVersion { get; }

      /// <summary>Id of the request being processed.</summary>
      string ApprovalRequestId { get; }

      /// <summary>The submitter of this request.</summary>
      string RequesterId { get; }

   }

   /// <summary>
   /// Extra info when the module is asked to decide something about one step.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   public interface IApprovalStepContext<out TServices, out TKey> : IApprovalContext<TServices, TKey>
   {
      /// <summary>Name of the step being processed.</summary>
      string StepName { get; }

      /// <summary>Who is taking the decision, or empty when nobody yet.</summary>
      string? SignerId { get; }
   }

   /// <summary>
   /// A step's input, seen without its input type. Used by the engine for things that are the same for
   /// every input - knowing that a step really asks for input, and getting the positions of its boxes.
   /// </summary>
   /// <remarks>
   /// Modules do not implement this interface themselves; what a module writes is
   /// <see cref="StepInput{TServices,TKey,TPayload}"/>, which already carries the input type.
   /// </remarks>
   public interface IApprovalStepInput
   {
      /// <summary>Position of every input box on the PDF, in order as declared by the module.</summary>
      IReadOnlyList<ApprovalSlot> FieldSlots { get; }

      /// <summary>Kind of every input box, parallel to <see cref="FieldSlots"/>.</summary>
      IReadOnlyList<ApprovalInputFieldKind> FieldKinds { get; }
   }

   /// <summary>
   /// A step's input as the engine sees it when the step is decided: validating the submitted input and
   /// writing its effect, without the engine needing to know the input type.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   /// <remarks>
   /// Called by the engine only. Modules do not implement or call it; what a module writes is
   /// <see cref="StepInput{TServices,TKey,TPayload}"/>, which already carries the implementation.
   /// </remarks>
   public interface IApprovalStepInput<TServices, TKey> : IApprovalStepInput
   {
      /// <summary>
      /// Reads the submitted input, validates it on the server, then takes the value of each input box to be
      /// drawn on the PDF.
      /// </summary>
      /// <param name="context">Info about the step being decided.</param>
      /// <param name="payloadJson">The input submitted by the signer, as JSON.</param>
      /// <returns>The value of each input box, parallel to <see cref="IApprovalStepInput.FieldSlots"/>.</returns>
      /// <exception cref="Em.Shared.ActionException">
      /// 400 when the input is missing or unreadable; otherwise whatever the module's check throws.
      /// </exception>
      Task<IReadOnlyList<ApprovalInputValue>> ValidateAsync(IApprovalStepContext<TServices, TKey> context,
         string? payloadJson);

      /// <summary>
      /// Writes the effect of the input to its document, inside the decision transaction.
      /// </summary>
      /// <param name="context">Info about the step being decided.</param>
      /// <param name="payloadJson">The input submitted by the signer, as JSON.</param>
      Task SignedAsync(IApprovalStepContext<TServices, TKey> context, string? payloadJson);
   }

   /// <summary>Value of one input box drawn on the PDF after its step is decided.</summary>
   /// <param name="Text">Text of the box, for a text box.</param>
   /// <param name="Checked">Whether the box is checked, for a checkbox.</param>
   public record ApprovalInputValue(string? Text, bool Checked);

   /// <summary>
   /// Input that a step asks for before it can be decided: what is shown, how it is validated, what is
   /// written to the document, and where it is drawn on the PDF.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   /// <typeparam name="TPayload">The input the signer sends back.</typeparam>
   public class StepInput<TServices, TKey, TPayload> : IApprovalStepInput<TServices, TKey>
   {
      /// <summary>
      /// Validates the submitted input, on the server, before its decision is written. Validation on the
      /// screen does not replace this.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, TPayload, Task>? Validate { get; set; }

      /// <summary>
      /// Writes the effect of the input to its document, inside the decision transaction. A failure here
      /// cancels the signature too, so there is never a signature whose effect was not written.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, TPayload, Task>? OnSigned { get; set; }

      /// <summary>
      /// Where the input is drawn on the PDF. One entry per input box.
      /// </summary>
      public List<ApprovalInputField<TPayload>> Fields { get; } = [];

      /// <summary>
      /// Adds one text box to the PDF, then returns this input so the next box can be chained directly.
      /// </summary>
      /// <param name="slot">Where the box is drawn.</param>
      /// <param name="text">How to get its text from the submitted input.</param>
      public StepInput<TServices, TKey, TPayload> Text(ApprovalSlot slot, Func<TPayload, string?> text) {
         ArgumentNullException.ThrowIfNull(slot);
         ArgumentNullException.ThrowIfNull(text);
         Fields.Add(new ApprovalInputField<TPayload> {
            Kind = ApprovalInputFieldKind.Text,
            Slot = slot,
            Text = text
         });
         return this;
      }

      /// <summary>
      /// Adds one checkbox to the PDF, then returns this input so the next box can be chained directly.
      /// </summary>
      /// <param name="slot">Where the box is drawn.</param>
      /// <param name="checked">How to decide whether the box is checked.</param>
      public StepInput<TServices, TKey, TPayload> Check(ApprovalSlot slot, Func<TPayload, bool> @checked) {
         ArgumentNullException.ThrowIfNull(slot);
         ArgumentNullException.ThrowIfNull(@checked);
         Fields.Add(new ApprovalInputField<TPayload> {
            Kind = ApprovalInputFieldKind.Check,
            Slot = slot,
            Checked = @checked
         });
         return this;
      }

      private static readonly System.Text.Json.JsonSerializerOptions PayloadOptions = new() {
         PropertyNameCaseInsensitive = true
      };

      async Task<IReadOnlyList<ApprovalInputValue>> IApprovalStepInput<TServices, TKey>.ValidateAsync(
         IApprovalStepContext<TServices, TKey> context, string? payloadJson) {
         var payload = ReadPayload(payloadJson);

         if (Validate is not null) await Validate(context, payload);

         return [.. Fields.Select(field => new ApprovalInputValue(
            field.Kind == ApprovalInputFieldKind.Text ? field.Text?.Invoke(payload) : null,
            field.Kind == ApprovalInputFieldKind.Check && field.Checked is not null && field.Checked(payload)))];
      }

      async Task IApprovalStepInput<TServices, TKey>.SignedAsync(IApprovalStepContext<TServices, TKey> context,
         string? payloadJson) {
         if (OnSigned is null) return;

         await OnSigned(context, ReadPayload(payloadJson));
      }

      private static TPayload ReadPayload(string? payloadJson) {
         if (string.IsNullOrWhiteSpace(payloadJson)) {
            throw new Em.Shared.ActionException("This step asks for input, but none was sent.", 400);
         }

         try {
            return System.Text.Json.JsonSerializer.Deserialize<TPayload>(payloadJson, PayloadOptions) ??
                   throw new Em.Shared.ActionException("This step asks for input, but the input sent is empty.", 400);
         }
         catch (System.Text.Json.JsonException ex) {
            throw new Em.Shared.ActionException($"The input sent for this step cannot be read: {ex.Message}", 400);
         }
      }

      /// <inheritdoc />
      public IReadOnlyList<ApprovalSlot> FieldSlots => Fields.Select(r => r.Slot).ToArray();

      /// <inheritdoc />
      public IReadOnlyList<ApprovalInputFieldKind> FieldKinds => Fields.Select(r => r.Kind).ToArray();
   }

   /// <summary>One input box on the PDF together with how to get its value from the submitted input.</summary>
   /// <typeparam name="TPayload">The input submitted by the signer.</typeparam>
   public class ApprovalInputField<TPayload>
   {
      /// <summary>Kind of the box: checkmark or text.</summary>
      public ApprovalInputFieldKind Kind { get; set; }

      /// <summary>Where the box is drawn.</summary>
      public ApprovalSlot Slot { get; set; } = new(0, 0);

      /// <summary>How to get this box's text value from the submitted input.</summary>
      public Func<TPayload, string?>? Text { get; set; }

      /// <summary>How to decide whether this checkbox is checked.</summary>
      public Func<TPayload, bool>? Checked { get; set; }
   }

   /// <summary>Kind of input box on the PDF.</summary>
   public enum ApprovalInputFieldKind
   {
      /// <summary>Checkbox.</summary>
      Check = 0,

      /// <summary>Text box.</summary>
      Text = 1
   }
}
