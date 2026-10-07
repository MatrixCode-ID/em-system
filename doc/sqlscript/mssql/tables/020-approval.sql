/*
    020 - Approval engine tables: document types (ta_Doc) and the seven approval request tables.

    Run on the target database (USE [DatabaseName]) after tables/010-core.sql (the foreign keys
    of the signer, the requester, and on-behalf-of refer to ta_User). Part 1 is meant for a new
    database; parts 2 and 3 are safe to run again (each table, index, and foreign key is only
    created when it does not exist yet).

    The document type rows in ta_Doc are NOT created here: they are filled in by the application or
    module that uses them (e.g. tables/900-emtest.sql). An approval request for a document type
    that is not registered is rejected by the FK.

    The two things below are part of the engine's BEHAVIOR, not an optimization:
      - the filtered unique index (document type, key, version) WHERE Stage = 1, which guards against
        two submissions racing at the same time - the engine relies on it, not only on a check in code;
      - the foreign keys from the signer/requester/on-behalf-of to ta_User, which make a system account
        (the built-in admin and the debugger, whose ids are not valid ULIDs) fail when it tries to sign.

    Guide: doc/engine/engine-approval.md.
*/

/* Required for the filtered index; sqlcmd runs batches with QUOTED_IDENTIFIER
   OFF, and without this the index creation fails with "SET options have incorrect
   settings". */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ============================================================================
-- 1. Document types (ta_Doc)
-- ============================================================================

-- ----------------------------
-- Table structure for ta_Doc
-- ----------------------------
CREATE TABLE [dbo].[ta_Doc] (
  [cDocName] varchar(25) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cDocDesc] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cDocAbv] char(7) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_Doc] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Primary Key structure for table ta_Doc
-- ----------------------------
ALTER TABLE [dbo].[ta_Doc] ADD CONSTRAINT [PK__ta_Doc__567BB9008034C094] PRIMARY KEY CLUSTERED ([cDocName])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- The document abbreviation becomes the Id prefix of every document, so two document types with the
-- same abbreviation could no longer be told apart.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'UX_ta_Doc_cDocAbv'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_Doc]'))
BEGIN
   IF EXISTS (SELECT [cDocAbv] FROM [dbo].[ta_Doc] GROUP BY [cDocAbv] HAVING COUNT(*) > 1)
   BEGIN
      -- cDocAbv is not a unicode column, and STRING_AGG rejects a unicode separator for it.
      DECLARE @dup nvarchar(max) =
         (SELECT STRING_AGG(CAST([cDocAbv] AS nvarchar(7)), N', ')
            FROM (SELECT [cDocAbv] FROM [dbo].[ta_Doc] GROUP BY [cDocAbv] HAVING COUNT(*) > 1) d);
      RAISERROR (N'Two or more document types share an abbreviation (%s), so the unique index was not created. Give each one an abbreviation of its own first.', 16, 1, @dup);
   END
   ELSE
   BEGIN
      CREATE UNIQUE NONCLUSTERED INDEX [UX_ta_Doc_cDocAbv] ON [dbo].[ta_Doc] ([cDocAbv]);
      PRINT N'Created UX_ta_Doc_cDocAbv.';
   END
END
GO

-- ============================================================================
-- 2. Approval request tables
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1. ta_ApprovalRequest - the request header
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'[dbo].[ta_ApprovalRequest]', N'U') IS NULL
BEGIN
   CREATE TABLE [dbo].[ta_ApprovalRequest] (
      [cApprovalRequestId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT [dbo].[NewUlidString]() NOT NULL,
      [cApprovalRequestKind] int NOT NULL,
      [cApprovalRequestDocType] varchar(25) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestDocKey] nvarchar(450) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestDocVersion] nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestRequesterId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestLevel] int DEFAULT (1) NOT NULL,
      [cApprovalRequestCompletedDate] datetime NULL,
      [cApprovalRequestPdfKey] varchar(1024) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestReinstateOf] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestStage] int DEFAULT (0) NOT NULL,
      [cApprovalRequestNote] nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestDate] datetime NOT NULL,
      [ustamp] datetime NOT NULL,
      [datestamp] datetime NOT NULL,
      [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      CONSTRAINT [PK_ta_ApprovalRequest] PRIMARY KEY CLUSTERED ([cApprovalRequestId])
   );
   PRINT N'Created ta_ApprovalRequest.';
END
GO

-- ----------------------------------------------------------------------------
-- 2. ta_ApprovalRequestStep - one row per step
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'[dbo].[ta_ApprovalRequestStep]', N'U') IS NULL
BEGIN
   CREATE TABLE [dbo].[ta_ApprovalRequestStep] (
      [cApprovalRequestStepId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT [dbo].[NewUlidString]() NOT NULL,
      [cApprovalRequestId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestStepName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestStepClaim] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestStepLevel] int NOT NULL,
      [cApprovalRequestStepOrder] int DEFAULT (1) NOT NULL,
      [cApprovalRequestStepStage] int DEFAULT (0) NOT NULL,
      [cApprovalRequestStepSignerId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestStepSignedDate] datetime NULL,
      [cApprovalRequestStepSignerRole] int DEFAULT (0) NOT NULL,
      [cApprovalRequestStepOnBehalfId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestStepNote] nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestStepVerificationCode] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestStepGuardReason] nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [ustamp] datetime NOT NULL,
      [datestamp] datetime NOT NULL,
      [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      CONSTRAINT [PK_ta_ApprovalRequestStep] PRIMARY KEY CLUSTERED ([cApprovalRequestStepId])
   );
   PRINT N'Created ta_ApprovalRequestStep.';
END
GO

-- ----------------------------------------------------------------------------
-- 3. ta_ApprovalRequestStepSigner - the signers assigned at submit
--    Deliberately lean: no standard columns, a composite key of the three columns.
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'[dbo].[ta_ApprovalRequestStepSigner]', N'U') IS NULL
BEGIN
   CREATE TABLE [dbo].[ta_ApprovalRequestStepSigner] (
      [cApprovalRequestId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestStepName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cUserId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      CONSTRAINT [PK_ta_ApprovalRequestStepSigner] PRIMARY KEY CLUSTERED
         ([cApprovalRequestId], [cApprovalRequestStepName], [cUserId])
   );
   PRINT N'Created ta_ApprovalRequestStepSigner.';
END
GO

-- ----------------------------------------------------------------------------
-- 4. ta_ApprovalRequestItem - the entities proposed to change
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'[dbo].[ta_ApprovalRequestItem]', N'U') IS NULL
BEGIN
   CREATE TABLE [dbo].[ta_ApprovalRequestItem] (
      [cApprovalRequestItemId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT [dbo].[NewUlidString]() NOT NULL,
      [cApprovalRequestId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestItemEntity] varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestItemKey] nvarchar(450) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestItemOperation] int NOT NULL,
      [cApprovalRequestItemOrder] int DEFAULT (0) NOT NULL,
      [cApprovalRequestItemStage] int DEFAULT (0) NOT NULL,
      [cApprovalRequestItemNote] nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [ustamp] datetime NOT NULL,
      [datestamp] datetime NOT NULL,
      [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      CONSTRAINT [PK_ta_ApprovalRequestItem] PRIMARY KEY CLUSTERED ([cApprovalRequestItemId])
   );
   PRINT N'Created ta_ApprovalRequestItem.';
END
GO

-- ----------------------------------------------------------------------------
-- 5. ta_ApprovalRequestItemKey - the parts of an entity's key
--    Lean: no standard columns, a composite key of entity + part name.
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'[dbo].[ta_ApprovalRequestItemKey]', N'U') IS NULL
BEGIN
   CREATE TABLE [dbo].[ta_ApprovalRequestItemKey] (
      [cApprovalRequestItemId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestItemKeyName] varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestItemKeyValue] nvarchar(450) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestItemKeyOrder] int DEFAULT (1) NOT NULL,
      CONSTRAINT [PK_ta_ApprovalRequestItemKey] PRIMARY KEY CLUSTERED
         ([cApprovalRequestItemId], [cApprovalRequestItemKeyName])
   );
   PRINT N'Created ta_ApprovalRequestItemKey.';
END
GO

-- ----------------------------------------------------------------------------
-- 6. ta_ApprovalRequestItemField - the columns proposed to change
--    Lean: no standard columns, a composite key of entity + column name.
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'[dbo].[ta_ApprovalRequestItemField]', N'U') IS NULL
BEGIN
   CREATE TABLE [dbo].[ta_ApprovalRequestItemField] (
      [cApprovalRequestItemId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestItemFieldName] varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestItemFieldOldValue] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestItemFieldNewValue] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestItemFieldCurrentValue] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      [cApprovalRequestItemFieldOrder] int DEFAULT (0) NOT NULL,
      CONSTRAINT [PK_ta_ApprovalRequestItemField] PRIMARY KEY CLUSTERED
         ([cApprovalRequestItemId], [cApprovalRequestItemFieldName])
   );
   PRINT N'Created ta_ApprovalRequestItemField.';
END
GO

-- ----------------------------------------------------------------------------
-- 7. ta_ApprovalRequestComment - free comments
-- ----------------------------------------------------------------------------
IF OBJECT_ID(N'[dbo].[ta_ApprovalRequestComment]', N'U') IS NULL
BEGIN
   CREATE TABLE [dbo].[ta_ApprovalRequestComment] (
      [cApprovalRequestCommentId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT [dbo].[NewUlidString]() NOT NULL,
      [cApprovalRequestId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cUserId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestCommentNote] nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
      [cApprovalRequestCommentDate] datetime NOT NULL,
      [ustamp] datetime NOT NULL,
      [datestamp] datetime NOT NULL,
      [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
      CONSTRAINT [PK_ta_ApprovalRequestComment] PRIMARY KEY CLUSTERED ([cApprovalRequestCommentId])
   );
   PRINT N'Created ta_ApprovalRequestComment.';
END
GO

-- ----------------------------------------------------------------------------
-- 8. Indexes
-- ----------------------------------------------------------------------------

/* Guard against two submissions racing at the same time for the same document + version.
   Part of the engine's behavior: without this index two requests could both pass the
   check in code and both be saved as waiting. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'UX_ta_ApprovalRequest_Pending'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequest]'))
BEGIN
   CREATE UNIQUE NONCLUSTERED INDEX [UX_ta_ApprovalRequest_Pending]
      ON [dbo].[ta_ApprovalRequest] ([cApprovalRequestDocType], [cApprovalRequestDocKey], [cApprovalRequestDocVersion])
      WHERE [cApprovalRequestStage] = 1;
   PRINT N'Created UX_ta_ApprovalRequest_Pending.';
END
GO

-- All requests of one document, including finished and withdrawn ones.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequest_Doc'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequest]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequest_Doc]
      ON [dbo].[ta_ApprovalRequest] ([cApprovalRequestDocType], [cApprovalRequestDocKey])
      INCLUDE ([cApprovalRequestDocVersion], [cApprovalRequestStage], [cApprovalRequestDate]);
   PRINT N'Created IX_ta_ApprovalRequest_Doc.';
END
GO

-- The paged list of requests, filtered by stage then document type.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequest_Stage'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequest]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequest_Stage]
      ON [dbo].[ta_ApprovalRequest] ([cApprovalRequestStage], [cApprovalRequestDocType], [cApprovalRequestDate]);
   PRINT N'Created IX_ta_ApprovalRequest_Stage.';
END
GO

-- The requests submitted by one user.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequest_Requester'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequest]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequest_Requester]
      ON [dbo].[ta_ApprovalRequest] ([cApprovalRequestRequesterId])
      INCLUDE ([cApprovalRequestStage], [cApprovalRequestDate]);
   PRINT N'Created IX_ta_ApprovalRequest_Requester.';
END
GO

-- The steps of one request, and the steps at the level that is running.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequestStep_Request'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequestStep]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequestStep_Request]
      ON [dbo].[ta_ApprovalRequestStep]
         ([cApprovalRequestId], [cApprovalRequestStepLevel], [cApprovalRequestStepOrder])
      INCLUDE ([cApprovalRequestStepStage], [cApprovalRequestStepName]);
   PRINT N'Created IX_ta_ApprovalRequestStep_Request.';
END
GO

/* The task list: steps that are still waiting, matched with the claims the user
   holds. Filtered to waiting ones only, because only those rows are ever looked
   up this way. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequestStep_Waiting'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequestStep]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequestStep_Waiting]
      ON [dbo].[ta_ApprovalRequestStep] ([cApprovalRequestStepClaim], [cApprovalRequestStepLevel])
      INCLUDE ([cApprovalRequestId], [cApprovalRequestStepName])
      WHERE [cApprovalRequestStepStage] = 0;
   PRINT N'Created IX_ta_ApprovalRequestStep_Waiting.';
END
GO

-- The signatures of one user, for history and for the four-eyes check.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequestStep_Signer'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequestStep]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequestStep_Signer]
      ON [dbo].[ta_ApprovalRequestStep] ([cApprovalRequestStepSignerId])
      INCLUDE ([cApprovalRequestId], [cApprovalRequestStepName])
      WHERE [cApprovalRequestStepSignerId] IS NOT NULL;
   PRINT N'Created IX_ta_ApprovalRequestStep_Signer.';
END
GO

-- The task list: steps whose signer is assigned per person.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequestStepSigner_User'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequestStepSigner]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequestStepSigner_User]
      ON [dbo].[ta_ApprovalRequestStepSigner] ([cUserId])
      INCLUDE ([cApprovalRequestId], [cApprovalRequestStepName]);
   PRINT N'Created IX_ta_ApprovalRequestStepSigner_User.';
END
GO

-- The proposed entities of one request, in the order they are applied.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequestItem_Request'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequestItem]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequestItem_Request]
      ON [dbo].[ta_ApprovalRequestItem] ([cApprovalRequestId], [cApprovalRequestItemOrder])
      INCLUDE ([cApprovalRequestItemEntity], [cApprovalRequestItemKey], [cApprovalRequestItemStage]);
   PRINT N'Created IX_ta_ApprovalRequestItem_Request.';
END
GO

/* The history of proposals on one entity - used to see who changed what
   on a data row. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequestItem_Entity'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequestItem]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequestItem_Entity]
      ON [dbo].[ta_ApprovalRequestItem] ([cApprovalRequestItemEntity], [cApprovalRequestItemKey])
      INCLUDE ([cApprovalRequestId], [cApprovalRequestItemStage]);
   PRINT N'Created IX_ta_ApprovalRequestItem_Entity.';
END
GO

-- The comments of one request, ordered by time.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequestComment_Request'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequestComment]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequestComment_Request]
      ON [dbo].[ta_ApprovalRequestComment] ([cApprovalRequestId], [cApprovalRequestCommentDate])
      INCLUDE ([cUserId]);
   PRINT N'Created IX_ta_ApprovalRequestComment_Request.';
END
GO

-- ----------------------------------------------------------------------------
-- 9. Foreign keys
--
--    To ta_User: NO ACTION on purpose. A user is never removed (only marked
--    inactive), and a signature must not disappear if someone later tries to
--    remove them. This FK is what makes a system account fail when it tries
--    to sign - see the note at the head of the file.
--
--    To the parent request/entity: CASCADE, so removing one request removes
--    all its content and leaves no dangling rows.
-- ----------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequest_ta_Doc')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequest] ADD CONSTRAINT [FK_ta_ApprovalRequest_ta_Doc]
      FOREIGN KEY ([cApprovalRequestDocType]) REFERENCES [dbo].[ta_Doc] ([cDocName]);
   PRINT N'Created FK_ta_ApprovalRequest_ta_Doc.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequest_ta_User')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequest] ADD CONSTRAINT [FK_ta_ApprovalRequest_ta_User]
      FOREIGN KEY ([cApprovalRequestRequesterId]) REFERENCES [dbo].[ta_User] ([cUserId]);
   PRINT N'Created FK_ta_ApprovalRequest_ta_User.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequest_ReinstateOf')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequest] ADD CONSTRAINT [FK_ta_ApprovalRequest_ReinstateOf]
      FOREIGN KEY ([cApprovalRequestReinstateOf]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId]);
   PRINT N'Created FK_ta_ApprovalRequest_ReinstateOf.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestStep_ta_ApprovalRequest')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestStep] ADD CONSTRAINT [FK_ta_ApprovalRequestStep_ta_ApprovalRequest]
      FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId])
      ON DELETE CASCADE ON UPDATE CASCADE;
   PRINT N'Created FK_ta_ApprovalRequestStep_ta_ApprovalRequest.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestStep_Signer')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestStep] ADD CONSTRAINT [FK_ta_ApprovalRequestStep_Signer]
      FOREIGN KEY ([cApprovalRequestStepSignerId]) REFERENCES [dbo].[ta_User] ([cUserId]);
   PRINT N'Created FK_ta_ApprovalRequestStep_Signer.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestStep_OnBehalf')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestStep] ADD CONSTRAINT [FK_ta_ApprovalRequestStep_OnBehalf]
      FOREIGN KEY ([cApprovalRequestStepOnBehalfId]) REFERENCES [dbo].[ta_User] ([cUserId]);
   PRINT N'Created FK_ta_ApprovalRequestStep_OnBehalf.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestStepSigner_ta_ApprovalRequest')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestStepSigner]
      ADD CONSTRAINT [FK_ta_ApprovalRequestStepSigner_ta_ApprovalRequest]
      FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId])
      ON DELETE CASCADE ON UPDATE CASCADE;
   PRINT N'Created FK_ta_ApprovalRequestStepSigner_ta_ApprovalRequest.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestStepSigner_ta_User')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestStepSigner] ADD CONSTRAINT [FK_ta_ApprovalRequestStepSigner_ta_User]
      FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]);
   PRINT N'Created FK_ta_ApprovalRequestStepSigner_ta_User.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestItem_ta_ApprovalRequest')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestItem] ADD CONSTRAINT [FK_ta_ApprovalRequestItem_ta_ApprovalRequest]
      FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId])
      ON DELETE CASCADE ON UPDATE CASCADE;
   PRINT N'Created FK_ta_ApprovalRequestItem_ta_ApprovalRequest.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestItemKey_ta_ApprovalRequestItem')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestItemKey]
      ADD CONSTRAINT [FK_ta_ApprovalRequestItemKey_ta_ApprovalRequestItem]
      FOREIGN KEY ([cApprovalRequestItemId]) REFERENCES [dbo].[ta_ApprovalRequestItem] ([cApprovalRequestItemId])
      ON DELETE CASCADE ON UPDATE CASCADE;
   PRINT N'Created FK_ta_ApprovalRequestItemKey_ta_ApprovalRequestItem.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestItemField_ta_ApprovalRequestItem')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestItemField]
      ADD CONSTRAINT [FK_ta_ApprovalRequestItemField_ta_ApprovalRequestItem]
      FOREIGN KEY ([cApprovalRequestItemId]) REFERENCES [dbo].[ta_ApprovalRequestItem] ([cApprovalRequestItemId])
      ON DELETE CASCADE ON UPDATE CASCADE;
   PRINT N'Created FK_ta_ApprovalRequestItemField_ta_ApprovalRequestItem.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestComment_ta_ApprovalRequest')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestComment] ADD CONSTRAINT [FK_ta_ApprovalRequestComment_ta_ApprovalRequest]
      FOREIGN KEY ([cApprovalRequestId]) REFERENCES [dbo].[ta_ApprovalRequest] ([cApprovalRequestId])
      ON DELETE CASCADE ON UPDATE CASCADE;
   PRINT N'Created FK_ta_ApprovalRequestComment_ta_ApprovalRequest.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ta_ApprovalRequestComment_ta_User')
BEGIN
   ALTER TABLE [dbo].[ta_ApprovalRequestComment] ADD CONSTRAINT [FK_ta_ApprovalRequestComment_ta_User]
      FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]);
   PRINT N'Created FK_ta_ApprovalRequestComment_ta_User.';
END
GO
