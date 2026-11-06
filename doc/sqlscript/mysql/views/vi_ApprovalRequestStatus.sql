-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_ApprovalRequestStatus AS
SELECT r.cApprovalRequestId, r.cApprovalRequestKind, r.cApprovalName, r.cSubjectKey,
       r.cSummary, r.cSubSummary, r.cApprovalRequestStage, r.cApprovalRequestLevel,
       r.cApprovalRequestCompletedDate,
       (SELECT GROUP_CONCAT(s.cApprovalRequestStepName SEPARATOR ', ')
        FROM ta_ApprovalRequestStep s
        WHERE s.cApprovalRequestId=r.cApprovalRequestId
          AND s.cApprovalRequestStepLevel=r.cApprovalRequestLevel
          AND s.cApprovalRequestStepStage=0 AND r.cApprovalRequestStage=1) AS cvApprovalActiveStep
FROM ta_ApprovalRequest r;
