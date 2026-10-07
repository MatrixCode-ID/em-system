using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// One approval request: which document was submitted, who submitted it, which level it has reached,
   /// and how it ended.
   /// </summary>
   /// <remarks>
   /// An approved request is also the audit trail of its change, so rows here are never discarded -
   /// including rejected or withdrawn ones.
   /// </remarks>
   [Table("ta_ApprovalRequest")]
   public class ta_ApprovalRequest
   {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Whether the proposed data is carried by this request or already lives in the document.</summary>
      public ApprovalKind cApprovalRequestKind { get; set; }

      /// <summary>Submitted document type, referring to the application's list of document types.</summary>
      public string cApprovalRequestDocType { get; set; } = string.Empty;

      /// <summary>
      /// Document key in canonical form: a JSON array of the key parts in the order declared by the
      /// module. One string so it can be indexed, matched and traced through history, even though the
      /// original key has several parts.
      /// </summary>
      public string cApprovalRequestDocKey { get; set; } = string.Empty;

      /// <summary>
      /// Submitted document version. Stored as text as is, because version numbering of legacy documents
      /// is not always numeric.
      /// </summary>
      public string cApprovalRequestDocVersion { get; set; } = string.Empty;

      /// <summary>Requester of this request. Always a real user - system accounts cannot submit.</summary>
      public string cApprovalRequestRequesterId { get; set; } = string.Empty;

      /// <summary>
      /// Level currently waiting for a decision. Steps are grouped per level, and the next level starts only
      /// after every step of this level is approved.
      /// </summary>
      public int cApprovalRequestLevel { get; set; }

      /// <summary>When all steps finished, or empty if they have not.</summary>
      public DateTime? cApprovalRequestCompletedDate { get; set; }

      /// <summary>
      /// Pointer to this document's base PDF file in binary storage, frozen at submission. Empty for
      /// document types without a PDF.
      /// </summary>
      public string? cApprovalRequestPdfKey { get; set; }

      /// <summary>
      /// The previous request that was withdrawn and then resubmitted for the same document and version.
      /// This link keeps the resubmission history connected.
      /// </summary>
      public string? cApprovalRequestReinstateOf { get; set; }

      /// <summary>Life-cycle stage of this request.</summary>
      public ApprovalStage cApprovalRequestStage { get; set; }

      /// <summary>Requester's note at submission.</summary>
      public string? cApprovalRequestNote { get; set; }

      /// <summary>When this request was submitted.</summary>
      public DateTime cApprovalRequestDate { get; set; }

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>
      /// Module-owned summary columns, captured at submission. The module decides the content; the engine
      /// only stores it and uses it to filter and sort the request list. A single flat JSON object. Names
      /// starting with a dollar sign are reserved for the engine's own notes, for example who withdrew this
      /// request and why, and never appear as columns.
      /// </summary>
      public string? json_object { get; set; }
   }
}
