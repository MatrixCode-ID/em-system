-- Approval schema v2. Requires 010-core.sql. No document-type catalog is owned by the engine.
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalRequest', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalRequest] (
   [cApprovalRequestId] char(26) NOT NULL,
   [cApprovalRequestKind] tinyint NOT NULL,
   [cApprovalName] varchar(50) NOT NULL,
   [cSubjectKey] nvarchar(400) NULL,
   [cSummary] nvarchar(400) NOT NULL,
   [cApprovalRequestRequesterId] char(26) NOT NULL,
   [cApprovalRequestStage] int NOT NULL,
   [cApprovalRequestLevel] int NOT NULL,
   [cApprovalRequestCompletedDate] datetime2 NULL,
   [cReinstateOf] char(26) NULL,
   [cApprovalRequestNote] nvarchar(1000) NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalRequest] PRIMARY KEY ([cApprovalRequestId]),
   CONSTRAINT [FK_ApprovalRequest_0] FOREIGN KEY ([cApprovalRequestRequesterId]) REFERENCES [dbo].[ta_User] ([cUserId]),
   CONSTRAINT [FK_ApprovalRequest_1] FOREIGN KEY ([cReinstateOf]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalRequestItem', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalRequestItem] (
   [cApprovalRequestItemId] char(26) NOT NULL,
   [cApprovalRequestId] char(26) NOT NULL,
   [cApprovalRequestItemOrder] int NOT NULL,
   [cApprovalRequestItemEntity] nvarchar(400) NOT NULL,
   [cApprovalRequestItemOperation] int NOT NULL,
   [cSubjectKey] nvarchar(400) NULL,
   [cParentItem] char(26) NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalRequestItem] PRIMARY KEY ([cApprovalRequestItemId]),
   CONSTRAINT [FK_ApprovalRequestItem_0] FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId]),
   CONSTRAINT [FK_ApprovalRequestItem_1] FOREIGN KEY ([cParentItem]) REFERENCES [dbo].[ta_ApprovalRequestItem] ([cApprovalRequestItemId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalRequestItemField', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalRequestItemField] (
   [cApprovalRequestItemFieldId] char(26) NOT NULL,
   [cApprovalRequestItemId] char(26) NOT NULL,
   [cApprovalRequestItemFieldName] nvarchar(128) NOT NULL,
   [cApprovalRequestItemFieldOld] nvarchar(max) NULL,
   [cApprovalRequestItemFieldNew] nvarchar(max) NULL,
   [cApprovalRequestItemFieldCurrent] nvarchar(max) NULL,
   [cApprovalRequestItemFieldConflict] bit NOT NULL,
   [cApprovalRequestItemFieldOverrideReason] nvarchar(1000) NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalRequestItemField] PRIMARY KEY ([cApprovalRequestItemFieldId]),
   CONSTRAINT [FK_ApprovalRequestItemField_0] FOREIGN KEY ([cApprovalRequestItemId]) REFERENCES [dbo].[ta_ApprovalRequestItem] ([cApprovalRequestItemId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalRequestStep', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalRequestStep] (
   [cApprovalRequestStepId] char(26) NOT NULL,
   [cApprovalRequestId] char(26) NOT NULL,
   [cApprovalRequestStepLevel] int NOT NULL,
   [cApprovalRequestStepOrder] int NOT NULL,
   [cApprovalRequestStepName] nvarchar(100) NOT NULL,
   [cApprovalRequestStepKind] int NOT NULL,
   [cApprovalRequestStepStage] int NOT NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalRequestStep] PRIMARY KEY ([cApprovalRequestStepId]),
   CONSTRAINT [FK_ApprovalRequestStep_0] FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalRequestStepCandidate', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalRequestStepCandidate] (
   [cApprovalRequestStepCandidateId] char(26) NOT NULL,
   [cApprovalRequestStepId] char(26) NOT NULL,
   [cUserId] char(26) NOT NULL,
   [cApprovalRequestStepCandidateBackup] bit NOT NULL,
   [cApprovalRequestStepCandidateOrder] int NOT NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalRequestStepCandidate] PRIMARY KEY ([cApprovalRequestStepCandidateId]),
   CONSTRAINT [FK_ApprovalRequestStepCandidate_0] FOREIGN KEY ([cApprovalRequestStepId]) REFERENCES [dbo].[ta_ApprovalRequestStep] ([cApprovalRequestStepId]),
   CONSTRAINT [FK_ApprovalRequestStepCandidate_1] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalRequestSignature', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalRequestSignature] (
   [cApprovalRequestSignatureId] char(26) NOT NULL,
   [cApprovalRequestId] char(26) NOT NULL,
   [cApprovalRequestStepId] char(26) NULL,
   [cUserId] char(26) NOT NULL,
   [cApprovalRequestSignatureApproved] bit NOT NULL,
   [cApprovalRequestSignatureNote] nvarchar(1000) NULL,
   [cApprovalRequestSignatureCode] varchar(40) NOT NULL,
   [cApprovalRequestSignatureAdmin] bit NOT NULL,
   [cApprovalRequestSignatureBackup] bit NOT NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalRequestSignature] PRIMARY KEY ([cApprovalRequestSignatureId]),
   CONSTRAINT [FK_ApprovalRequestSignature_0] FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId]),
   CONSTRAINT [FK_ApprovalRequestSignature_1] FOREIGN KEY ([cApprovalRequestStepId]) REFERENCES [dbo].[ta_ApprovalRequestStep] ([cApprovalRequestStepId]),
   CONSTRAINT [FK_ApprovalRequestSignature_2] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalRequestComment', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalRequestComment] (
   [cApprovalRequestCommentId] char(26) NOT NULL,
   [cApprovalRequestId] char(26) NOT NULL,
   [cUserId] char(26) NOT NULL,
   [cApprovalRequestCommentText] nvarchar(1000) NOT NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalRequestComment] PRIMARY KEY ([cApprovalRequestCommentId]),
   CONSTRAINT [FK_ApprovalRequestComment_0] FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId]),
   CONSTRAINT [FK_ApprovalRequestComment_1] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalRequestAttachment', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalRequestAttachment] (
   [cApprovalRequestAttachmentId] char(26) NOT NULL,
   [cApprovalRequestId] char(26) NOT NULL,
   [cApprovalRequestCommentId] char(26) NULL,
   [cApprovalRequestAttachmentKey] nvarchar(400) NOT NULL,
   [cApprovalRequestAttachmentVersion] int NOT NULL,
   [cApprovalRequestAttachmentName] nvarchar(255) NOT NULL,
   [cApprovalRequestAttachmentSize] bigint NOT NULL,
   [cUserId] char(26) NOT NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalRequestAttachment] PRIMARY KEY ([cApprovalRequestAttachmentId]),
   CONSTRAINT [FK_ApprovalRequestAttachment_0] FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId]),
   CONSTRAINT [FK_ApprovalRequestAttachment_1] FOREIGN KEY ([cApprovalRequestCommentId]) REFERENCES [dbo].[ta_ApprovalRequestComment] ([cApprovalRequestCommentId]),
   CONSTRAINT [FK_ApprovalRequestAttachment_2] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalSignerSetting', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalSignerSetting] (
   [cApprovalSignerSettingId] char(26) NOT NULL,
   [cApprovalName] varchar(50) NOT NULL,
   [cApprovalSignerSettingStep] nvarchar(100) NOT NULL,
   [cApprovalSignerSettingDynamic] bit NOT NULL,
   [cUserId] char(26) NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalSignerSetting] PRIMARY KEY ([cApprovalSignerSettingId]),
   CONSTRAINT [FK_ApprovalSignerSetting_0] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId])
);
END;
GO

IF OBJECT_ID(N'dbo.ta_ApprovalSignerSettingBackup', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ApprovalSignerSettingBackup] (
   [cApprovalSignerSettingBackupId] char(26) NOT NULL,
   [cApprovalSignerSettingId] char(26) NOT NULL,
   [cUserId] char(26) NOT NULL,
   [cApprovalSignerSettingBackupOrder] int NOT NULL,
   [ustamp] datetime2 NOT NULL,
   [datestamp] datetime2 NOT NULL,
   [json_object] nvarchar(max) NULL,
   CONSTRAINT [PK_ta_ApprovalSignerSettingBackup] PRIMARY KEY ([cApprovalSignerSettingBackupId]),
   CONSTRAINT [FK_ApprovalSignerSettingBackup_0] FOREIGN KEY ([cApprovalSignerSettingId]) REFERENCES [dbo].[ta_ApprovalSignerSetting] ([cApprovalSignerSettingId]),
   CONSTRAINT [FK_ApprovalSignerSettingBackup_1] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId])
);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ApprovalSystemActive' AND object_id = OBJECT_ID(N'dbo.ta_ApprovalRequest'))
CREATE UNIQUE INDEX [UX_ApprovalSystemActive] ON [dbo].[ta_ApprovalRequest] ([cApprovalRequestKind], [cApprovalName], [cSubjectKey]) WHERE [cApprovalRequestKind] = 2 AND [cApprovalRequestStage] >= -1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ApprovalStepName' AND object_id = OBJECT_ID(N'dbo.ta_ApprovalRequestStep'))
CREATE UNIQUE INDEX [UX_ApprovalStepName] ON [dbo].[ta_ApprovalRequestStep] ([cApprovalRequestId], [cApprovalRequestStepName]) ;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ApprovalStepCandidate' AND object_id = OBJECT_ID(N'dbo.ta_ApprovalRequestStepCandidate'))
CREATE UNIQUE INDEX [UX_ApprovalStepCandidate] ON [dbo].[ta_ApprovalRequestStepCandidate] ([cApprovalRequestStepId], [cUserId]) ;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ApprovalField' AND object_id = OBJECT_ID(N'dbo.ta_ApprovalRequestItemField'))
CREATE UNIQUE INDEX [UX_ApprovalField] ON [dbo].[ta_ApprovalRequestItemField] ([cApprovalRequestItemId], [cApprovalRequestItemFieldName]) ;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ApprovalSignature' AND object_id = OBJECT_ID(N'dbo.ta_ApprovalRequestSignature'))
CREATE UNIQUE INDEX [UX_ApprovalSignature] ON [dbo].[ta_ApprovalRequestSignature] ([cApprovalRequestStepId]) WHERE [cApprovalRequestStepId] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ApprovalVerification' AND object_id = OBJECT_ID(N'dbo.ta_ApprovalRequestSignature'))
CREATE UNIQUE INDEX [UX_ApprovalVerification] ON [dbo].[ta_ApprovalRequestSignature] ([cApprovalRequestSignatureCode]) ;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ApprovalSetting' AND object_id = OBJECT_ID(N'dbo.ta_ApprovalSignerSetting'))
CREATE UNIQUE INDEX [UX_ApprovalSetting] ON [dbo].[ta_ApprovalSignerSetting] ([cApprovalName], [cApprovalSignerSettingStep]) ;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ApprovalSettingBackup' AND object_id = OBJECT_ID(N'dbo.ta_ApprovalSignerSettingBackup'))
CREATE UNIQUE INDEX [UX_ApprovalSettingBackup] ON [dbo].[ta_ApprovalSignerSettingBackup] ([cApprovalSignerSettingId], [cUserId]) ;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApprovalSubject' AND object_id=OBJECT_ID(N'dbo.ta_ApprovalRequest'))
CREATE INDEX IX_ApprovalSubject ON dbo.ta_ApprovalRequest(cApprovalRequestKind,cApprovalName,cSubjectKey);
GO
