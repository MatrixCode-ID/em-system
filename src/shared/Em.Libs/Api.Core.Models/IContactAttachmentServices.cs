using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>
/// Files attached to a contact; all HTTP calls require the Business Module Manager claim. Name, kind and note
/// are saved with the employee through <see cref="TenantEmpPerson.Attachments"/>; the actions here change files.
/// </summary>
public interface IContactAttachmentServices : IServices
{
   /// <summary>Managed bucket holding the files.</summary>
   public const string Bucket = "contact-attachments";

   #region Meta's
   /// <summary>Lists the active attachments of a contact in display order, with their file details.</summary>
   Task<ContactAttachmentInfo[]> GetMeta_ContactAttachments(string contactId);
   /// <summary>Stores a file and adds an attachment named after it.</summary>
   /// <param name="upload">Contact, file name and kind.</param>
   /// <param name="content">File content.</param>
   Task<ContactAttachmentInfo> PostGetMeta_ContactAttachment_Add(ContactAttachmentUpload upload, Stream content);
   /// <summary>Stores a new version of the file of an attachment, keeping its name, kind and note.</summary>
   /// <param name="upload">Attachment, its last-read stamp and the file name.</param>
   /// <param name="content">File content.</param>
   Task<ContactAttachmentInfo> PostGetMeta_ContactAttachment_Replace(ContactAttachmentUpload upload, Stream content);
   /// <summary>Deactivates an attachment. The file and its versions stay in object storage.</summary>
   Task PostMeta_ContactAttachment_Remove(string attachmentId, DateTime ustamp);
   /// <summary>Reads the current file of an active attachment.</summary>
   Task<Stream> GetMeta_ContactAttachment_Download(string attachmentId);
   #endregion
}
