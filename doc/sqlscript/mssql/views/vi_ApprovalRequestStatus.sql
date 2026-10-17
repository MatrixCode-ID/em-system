-- Stable, inexpensive join target for modules. Requires approval schema v2.
DROP VIEW IF EXISTS dbo.vi_ApprovalRequestStatus;
GO
CREATE VIEW dbo.vi_ApprovalRequestStatus AS
SELECT r.cApprovalRequestId, r.cApprovalRequestKind, r.cApprovalName, r.cSubjectKey,
       r.cSummary, r.cSubSummary, r.cApprovalRequestStage, r.cApprovalRequestLevel,
       r.cApprovalRequestCompletedDate,
       (SELECT STRING_AGG(s.cApprovalRequestStepName, N', ')
        FROM dbo.ta_ApprovalRequestStep s
        WHERE s.cApprovalRequestId=r.cApprovalRequestId
          AND s.cApprovalRequestStepLevel=r.cApprovalRequestLevel
          AND s.cApprovalRequestStepStage=0 AND r.cApprovalRequestStage=1) AS cvApprovalActiveStep
FROM dbo.ta_ApprovalRequest r;
GO
SELECT TOP (100) * FROM dbo.vi_ApprovalRequestStatus;
GO
