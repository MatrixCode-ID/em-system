using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Api.Core.ObjectStorage;
using Em.Shared;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>Files attached to a contact. The file lives in object storage; the record holds what describes it.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class ContactAttachmentServices(ApiCoreContext ctx, IObjectStore? store = null) : ServicesBase, IContactAttachmentServices
{
   private const string FileNameKey = "file-name";
   private const int MaxNameLength = 255;
   private static readonly FileExtensionContentTypeProvider MimeTypes = new();
   private IObjectStore Store => store ?? throw new ActionException("Object storage is not turned on for this server.", 503);

   #region Meta's
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<ContactAttachmentInfo[]> GetMeta_ContactAttachments(string contactId) {
      var rows = await ctx.ta_ContactAttachments.AsNoTracking()
         .Where(x => x.cContactId == contactId && x.cContactAttachmentState == ta_ContactAttachment.StateActive)
         .OrderBy(x => x.cContactAttachmentOrder).ThenBy(x => x.cContactAttachmentId).ToArrayAsync(AbortToken);
      var result = new ContactAttachmentInfo[rows.Length];
      for (var i = 0; i < rows.Length; i++) result[i] = await Describe(rows[i]);
      return result;
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<ContactAttachmentInfo> PostGetMeta_ContactAttachment_Add(ContactAttachmentUpload upload, Stream content) {
      ArgumentNullException.ThrowIfNull(upload);
      // Everything is checked before the content is read, so a refused upload does not transfer its body.
      var name = FileName(upload);
      if (!Enum.IsDefined(upload.Kind)) throw new ActionException("Choose an attachment kind.", 400);
      if (!await ctx.ta_Contacts.AnyAsync(x => x.cContactId == upload.ContactId, AbortToken)) throw new ActionException("The contact was not found.", 404);
      var id = Ulid.NewUlid().ToString();
      var info = await Put(upload.ContactId, id, name, content);
      var next = await ctx.ta_ContactAttachments.Where(x => x.cContactId == upload.ContactId).Select(x => (int?)x.cContactAttachmentOrder).MaxAsync(AbortToken) ?? 0;
      var now = DateTime.UtcNow;
      var row = new ta_ContactAttachment {
         cContactAttachmentId = id, cContactId = upload.ContactId, cStoreObjectId = info.Id, cContactAttachmentName = name, cContactAttachmentKind = upload.Kind,
         cContactAttachmentState = ta_ContactAttachment.StateActive, cContactAttachmentOrder = next + 1, ustamp = now, datestamp = now
      };
      ctx.ta_ContactAttachments.Add(row);
      Audit(id, "contact.attachment.created", "Added attachment '" + name + "'");
      await ctx.SaveChangesAsync(AbortToken);
      // Read back: the stored stamp is rounded by the database, and the caller's next save compares it.
      return await Describe(await Active(id));
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<ContactAttachmentInfo> PostGetMeta_ContactAttachment_Replace(ContactAttachmentUpload upload, Stream content) {
      ArgumentNullException.ThrowIfNull(upload);
      var name = FileName(upload);
      var row = await Active(upload.AttachmentId);
      if (row.ustamp != upload.Ustamp) throw new ActionException("The attachment changed. Refresh before replacing the file.", 409);
      await Put(row.cContactId, row.cContactAttachmentId, name, content);
      var stamp = TenantOrg.Stamp(row.ustamp);
      await ctx.ta_ContactAttachments.Where(x => x.cContactAttachmentId == row.cContactAttachmentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ustamp, stamp), AbortToken);
      Audit(row.cContactAttachmentId, "contact.attachment.changed", "Replaced the file of attachment '" + row.cContactAttachmentName + "'");
      await ctx.SaveChangesAsync(AbortToken);
      return await Describe(await Active(row.cContactAttachmentId));
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task PostMeta_ContactAttachment_Remove(string attachmentId, DateTime ustamp) {
      var row = await Active(attachmentId);
      if (row.ustamp != ustamp) throw new ActionException("The attachment changed. Refresh before removing it.", 409);
      await ctx.ta_ContactAttachments.Where(x => x.cContactAttachmentId == attachmentId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cContactAttachmentState, ta_ContactAttachment.StateInactive).SetProperty(x => x.ustamp, TenantOrg.Stamp(row.ustamp)), AbortToken);
      Audit(attachmentId, "contact.attachment.changed", "Removed attachment '" + row.cContactAttachmentName + "'");
      await ctx.SaveChangesAsync(AbortToken);
   }

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<Stream> GetMeta_ContactAttachment_Download(string attachmentId) {
      var row = await Active(attachmentId);
      return (await Store.OpenAsync(IContactAttachmentServices.Bucket, Key(row.cContactId, row.cContactAttachmentId), null, AbortToken)).Content;
   }
   #endregion

   private static string Key(string contactId, string attachmentId) => contactId + "/" + attachmentId;

   private static string FileName(ContactAttachmentUpload upload) {
      var name = Path.GetFileName(upload.FileName ?? "").Trim();
      if (name.Length == 0) throw new ActionException("The file has no name.", 400);
      if (name.Length > MaxNameLength) throw new ActionException("The file name is too long.", 400);
      return name;
   }

   private Task<StoreObjectInfo> Put(string contactId, string attachmentId, string fileName, Stream content) =>
      Store.PutAsync(IContactAttachmentServices.Bucket, Key(contactId, attachmentId), content,
         MimeTypes.TryGetContentType(fileName, out var mime) ? mime : null, new Dictionary<string, string> { [FileNameKey] = fileName }, AbortToken);

   private async Task<ta_ContactAttachment> Active(string attachmentId) =>
      await ctx.ta_ContactAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.cContactAttachmentId == attachmentId && x.cContactAttachmentState == ta_ContactAttachment.StateActive, AbortToken)
      ?? throw new ActionException("The attachment was not found.", 404);

   // File details stay in object storage. A host without it, or a missing file, still lists the record.
   private async Task<ContactAttachmentInfo> Describe(ta_ContactAttachment row) {
      var result = new ContactAttachmentInfo { Row = row };
      if (store is null) return result;
      var key = Key(row.cContactId, row.cContactAttachmentId);
      var info = await store.GetAsync(IContactAttachmentServices.Bucket, key, AbortToken);
      if (info is null || info.IsDeleteMarker) return result;
      var version = (await store.VersionsAsync(IContactAttachmentServices.Bucket, key, AbortToken)).FirstOrDefault(x => x.IsCurrent);
      result.FileName = version?.Metadata?.GetValueOrDefault(FileNameKey) ?? row.cContactAttachmentName;
      result.Mime = info.Mime; result.Size = info.Size; result.UploadedAt = info.ModifiedAt; result.Version = info.Version;
      return result;
   }

   private void Audit(string id, string kind, string summary) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "attachment"
      });
   }
}
