using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Contact attachment proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class ContactAttachmentClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), IContactAttachmentServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<ContactAttachmentInfo[]> GetMeta_ContactAttachments(string contactId) { Prepare(); return GetAsync<ContactAttachmentInfo[]>(nameof(GetMeta_ContactAttachments), contactId); }
   /// <inheritdoc />
   public Task<ContactAttachmentInfo> PostGetMeta_ContactAttachment_Add(ContactAttachmentUpload upload, Stream content) { Prepare(); return PostStreamAsync<ContactAttachmentInfo>(nameof(PostGetMeta_ContactAttachment_Add), content, upload); }
   /// <inheritdoc />
   public Task<ContactAttachmentInfo> PostGetMeta_ContactAttachment_Replace(ContactAttachmentUpload upload, Stream content) { Prepare(); return PostStreamAsync<ContactAttachmentInfo>(nameof(PostGetMeta_ContactAttachment_Replace), content, upload); }
   /// <inheritdoc />
   public Task PostMeta_ContactAttachment_Remove(string attachmentId, DateTime ustamp) { Prepare(); return PostAsync(nameof(PostMeta_ContactAttachment_Remove), attachmentId, ustamp); }
   /// <inheritdoc />
   public Task<Stream> GetMeta_ContactAttachment_Download(string attachmentId) { Prepare(); return GetStreamAsync(nameof(GetMeta_ContactAttachment_Download), attachmentId); }
}
