namespace Em.Api.Core.Models;
/// <summary>The mechanism responsible for an approval.</summary>
public enum ApprovalKind {
   /// <summary>Deferred changes to module-owned rows.</summary>
   Data = 1,
   /// <summary>Module-owned subject with a server-defined signing flow.</summary>
   System = 2,
   /// <summary>Reserved for physical-document approval; not implemented.</summary>
   Document = 3
}
/// <summary>Persistent request lifecycle.</summary>
public enum ApprovalStage {
   /// <summary>Voided after completion.</summary>
   ReinstatedAfterFinish = -3,
   /// <summary>Voided while pending, or withdrawn.</summary>
   Cancelled = -2,
   /// <summary>Rejected; a System request still occupies its subject until voided.</summary>
   Rejected = -1,
   /// <summary>Reserved for draft state.</summary>
   Draft = 0,
   /// <summary>Waiting for a decision.</summary>
   Pending = 1,
   /// <summary>Successfully completed.</summary>
   Approved = 2
}
/// <summary>State of a snapshotted signing step.</summary>
public enum ApprovalStepStatus {
   /// <summary>Rejected.</summary>
   Rejected = -1,
   /// <summary>Waiting.</summary>
   Waiting = 0,
   /// <summary>Signed.</summary>
   Approved = 1
}
/// <summary>Origin of a step's primary signer.</summary>
public enum ApprovalStepKind {
   /// <summary>A configured user.</summary>
   User = 0,
   /// <summary>A user supplied by the module when requested.</summary>
   Dynamic = 1
}
/// <summary>Proposed row operation.</summary>
public enum ApprovalItemOperation {
   /// <summary>Insert after approval.</summary>
   Create = 1,
   /// <summary>Compare and update tracked columns.</summary>
   Update = 2,
   /// <summary>Apply the declared deletion policy.</summary>
   Delete = 3,
   /// <summary>Undo a soft deletion.</summary>
   Restore = 4
}
