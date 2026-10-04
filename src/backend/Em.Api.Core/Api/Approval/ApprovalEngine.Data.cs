using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   // The data half of the engine: submitting a proposal for changes to data. The decision on a pending
   // proposal is in the decider, and both end in the same applier.
   internal sealed partial class ApprovalEngine
   {
      /// <inheritdoc />
      public async Task<ApprovalSubmitResult> SubmitDataAsync<TKey>(string docType, TKey docKey,
         IReadOnlyList<ApprovalDataItem> items, string? note = null)
         where TKey : notnull {
         ArgumentNullException.ThrowIfNull(docKey);
         ArgumentNullException.ThrowIfNull(items);

         var flow = _registry.Get(docType);
         if (flow is not IApprovalDataFlow data || flow.Kind != ApprovalKind.Data) {
            throw new InvalidOperationException(
               $"Document type '{flow.DocType}' is a document approval, so it is submitted with SubmitAsync.");
         }

         note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
         if (note is { Length: > MaxNoteLength }) {
            throw new ActionException($"The note is {note.Length} characters long; the limit is {MaxNoteLength}.", 400);
         }

         // 1. Who is asking: a real, active user, for the same reason as in a document submission.
         var requesterId = await new ApprovalAccess(_ctx).RequireRealUserAsync(Request);

         var canonicalKey = ApprovalKey.ToCanonical(docKey);
         var prepared = PrepareItems(flow, data, items);
         if (prepared.Count == 0) {
            throw new ActionException("There is nothing to change: every proposed value is the same as the one it replaces.", 400);
         }

         // 2. The holder of the approval claim does not wait for anybody: the proposal is applied at once,
         //    but it is still stored as a request that approved itself, with the same comparison against
         //    the table that anybody else's would get.
         var direct = ApprovalAccess.HoldsClaim(Request, data.ApproveClaim);

         var requestId = $"{Ulid.NewUlid()}";
         var now = DateTime.Now;
         var stamp = DateTime.UtcNow;

         var request = new ta_ApprovalRequest {
            cApprovalRequestId = requestId,
            cApprovalRequestKind = ApprovalKind.Data,
            cApprovalRequestDocType = flow.DocType,
            cApprovalRequestDocKey = canonicalKey,
            // Several proposals for the same record may wait at the same time, and the index that keeps
            // one request pending per document and version would forbid exactly that. A data request has
            // no version of its own, so its own id stands in: unique, and never shown as a version.
            cApprovalRequestDocVersion = requestId,
            cApprovalRequestRequesterId = requesterId,
            cApprovalRequestLevel = 1,
            cApprovalRequestStage = ApprovalStage.Pending,
            cApprovalRequestNote = note,
            cApprovalRequestDate = now,
            ustamp = stamp,
            datestamp = stamp
         };

         var asItems = prepared.Select(r => r.Item).ToList();
         var scope = new ApprovalRunScope(this, App.ServiceProvider,
            request, asItems);

         request.json_object = ApprovalRequestJson.Compose(await flow.SummaryAsync(scope), null);

         // 3. One transaction: the header, the step, the proposal, and - for the holder of the claim - the
         //    comparison, the application, and the finish.
         await using var transaction = await ApprovalTransaction.BeginAsync(_ctx, flow, App.ServiceProvider);
         try {
            // The header goes first on its own: the rows below point at it, the database enforces that,
            // and EF is not told about the foreign key, so it would not order them by itself.
            _ctx.ta_ApprovalRequests.Add(request);
            await _ctx.SaveChangesAsync();

            var step = new ta_ApprovalRequestStep {
               cApprovalRequestStepId = $"{Ulid.NewUlid()}",
               cApprovalRequestId = requestId,
               cApprovalRequestStepName = data.ApproveClaim.Name,
               cApprovalRequestStepClaim = data.ApproveClaim.Key,
               cApprovalRequestStepLevel = 1,
               cApprovalRequestStepOrder = 1,
               cApprovalRequestStepStage = ApprovalStepStatus.Waiting,
               ustamp = stamp,
               datestamp = stamp
            };
            _ctx.ta_ApprovalRequestSteps.Add(step);

            var rows = new List<ta_ApprovalRequestItem>();
            for (var i = 0; i < prepared.Count; i++) {
               rows.Add(new ta_ApprovalRequestItem {
                  cApprovalRequestItemId = $"{Ulid.NewUlid()}",
                  cApprovalRequestId = requestId,
                  cApprovalRequestItemEntity = prepared[i].Entity.Name,
                  cApprovalRequestItemKey = prepared[i].CanonicalKey,
                  cApprovalRequestItemOperation = prepared[i].Item.Operation,
                  cApprovalRequestItemOrder = i,
                  cApprovalRequestItemStage = ApprovalItemStage.Pending,
                  ustamp = stamp,
                  datestamp = stamp
               });
            }

            _ctx.ta_ApprovalRequestItems.AddRange(rows);
            await _ctx.SaveChangesAsync();

            for (var i = 0; i < prepared.Count; i++) {
               var itemId = rows[i].cApprovalRequestItemId;

               _ctx.ta_ApprovalRequestItemKeys.AddRange(ApprovalKey.GetKeyParts(prepared[i].Item.Key)
                  .Select(part => new ta_ApprovalRequestItemKey {
                     cApprovalRequestItemId = itemId,
                     cApprovalRequestItemKeyName = part.Name,
                     cApprovalRequestItemKeyValue = part.Value,
                     cApprovalRequestItemKeyOrder = part.Order
                  }));

               _ctx.ta_ApprovalRequestItemFields.AddRange(prepared[i].Item.Fields
                  .Select((field, index) => new ta_ApprovalRequestItemField {
                     cApprovalRequestItemId = itemId,
                     cApprovalRequestItemFieldName = field.Name,
                     cApprovalRequestItemFieldOldValue = field.OldValue,
                     cApprovalRequestItemFieldNewValue = field.NewValue,
                     cApprovalRequestItemFieldOrder = index
                  }));
            }

            await _ctx.SaveChangesAsync();

            if (direct) {
               step.cApprovalRequestStepStage = ApprovalStepStatus.Approved;
               step.cApprovalRequestStepSignerId = requesterId;
               step.cApprovalRequestStepSignedDate = now;
               step.cApprovalRequestStepSignerRole = ApprovalSignerRole.Assigned;
               step.cApprovalRequestStepVerificationCode = await ApprovalVerificationCode.CreateUnusedAsync(_ctx);

               var applied = await ApprovalDataApplier.ApplyAsync(_ctx, data, scope, false, null, CancellationToken.None);

               request.cApprovalRequestStage = ApprovalStage.Approved;
               request.cApprovalRequestCompletedDate = now;
               request.ustamp = stamp;

               scope = scope with { Items = applied };
               await flow.RunFinishingAsync(scope);
               await _ctx.SaveChangesAsync();
            }

            await transaction.CommitAsync();
         }
         catch (Exception ex) {
            await transaction.RollbackAsync();
            _ctx.ChangeTracker.Clear();

            // The caller is the one holding the claim and saving from a screen it loaded earlier: what it
            // is told is what changed under it, so it can reload and redo the edit.
            if (ex is ApprovalConflictException conflict) {
               throw new ActionException(
                  "Your changes were not saved, because the record was changed by somebody else after you opened it. " +
                  "Reload it and make the change again." + Environment.NewLine + conflict.Message, 409);
            }

            throw;
         }

         // Everything below happens after the request is safely stored, so none of it can undo it.
         await RunAfterCommitAsync(flow.RunSubmittedAsync(scope), "OnSubmitted", flow.DocType, requestId);
         if (direct) {
            await RunAfterCommitAsync(flow.RunFinishedAsync(scope), "OnFinished", flow.DocType, requestId);
         }

         return new ApprovalSubmitResult(requestId, direct);
      }

      // Checks what the module proposed against what the flow declares, and drops what proposes nothing.
      // A mistake here is a mistake of the module, not of the caller, so it is an exception that says what
      // is wrong rather than an answer with a status.
      private static List<PreparedItem> PrepareItems(ApprovalFlowDeclaration flow, IApprovalDataFlow data,
         IReadOnlyList<ApprovalDataItem> items) {
         var prepared = new List<PreparedItem>();
         var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

         foreach (var item in items) {
            var entity = data.FindEntity(item.Entity) ?? throw new InvalidOperationException(
               $"Data approval flow '{flow.DocType}' declares no entity named '{item.Entity}'.");

            if (item.Key.GetType() != entity.KeyType) {
               throw new InvalidOperationException(
                  $"Entity '{entity.Name}' is keyed by {entity.KeyType.Name}, but the key given is a {item.Key.GetType().Name}.");
            }

            var canonicalKey = ApprovalKey.ToCanonical(item.Key);
            if (!seen.Add($"{entity.Name}|{canonicalKey}")) {
               throw new InvalidOperationException(
                  $"Entity '{entity.Name}' with key {canonicalKey} is proposed twice in one request.");
            }

            var fields = new List<ApprovalDataField>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in item.Fields) {
               if (string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > MaxFieldNameLength) {
                  throw new InvalidOperationException(
                     $"A column of entity '{entity.Name}' has a name that is empty or longer than {MaxFieldNameLength} characters.");
               }

               if (!names.Add(field.Name)) {
                  throw new InvalidOperationException(
                     $"Column '{field.Name}' of entity '{entity.Name}' is proposed twice in one request.");
               }

               // A column whose proposed value is the one it already has proposes nothing.
               if (item.Operation == ApprovalItemOperation.Update &&
                   string.Equals(field.OldValue, field.NewValue, StringComparison.Ordinal)) {
                  continue;
               }

               fields.Add(field);
            }

            switch (item.Operation) {
               case ApprovalItemOperation.Create or ApprovalItemOperation.Update:
                  if (fields.Count == 0) {
                     // An update that changes nothing is dropped; a creation without a single column is a mistake.
                     if (item.Operation == ApprovalItemOperation.Create) {
                        throw new InvalidOperationException($"Creating entity '{entity.Name}' needs at least one column.");
                     }

                     continue;
                  }

                  break;

               case ApprovalItemOperation.Delete or ApprovalItemOperation.Reinstate:
                  if (fields.Count > 0) {
                     throw new InvalidOperationException(
                        $"{item.Operation} of entity '{entity.Name}' proposes no columns, but {fields.Count} were given.");
                  }

                  break;

               default:
                  throw new InvalidOperationException($"Unknown operation {item.Operation} for entity '{entity.Name}'.");
            }

            prepared.Add(new PreparedItem(entity, canonicalKey,
               new ApprovalDataItem(entity.Name, item.Key, item.Operation, fields)));
         }

         // The order the module declared the entities in decides what is applied first.
         return [.. prepared.Select((r, index) => (r, index)).OrderBy(r => r.r.Entity.Order).ThenBy(r => r.index).Select(r => r.r)];
      }

      private const int MaxFieldNameLength = 100;

      private sealed record PreparedItem(ApprovalEntityDeclaration Entity, string CanonicalKey, ApprovalDataItem Item);
   }
}
