/*
    Tables of the Em.Test test module (the complete set, run once on the core database).

    Run on the target database (USE [DatabaseName]). Safe to run again: each table and
    document type row is only created when it does not exist. Its views are in views/vi_TestItem.sql
    and views/vi_TestDoc.sql.

    Contents:
    - ta_TestItem               : simple master data to test CRUD, paging, UiModel, and data approval.
    - ta_TestDoc                : a transaction document to test document approval (PDF + stamp).
    - ta_Doc                    : the two document types the approval engine uses for this module
                                  (EmTestDoc, EmTestItem). Without these rows an approval request is rejected by the FK.

    Module documentation: doc/engine/engine-test-module.md
*/

SET XACT_ABORT ON;
GO

--region ta_TestItem
IF OBJECT_ID(N'[dbo].[ta_TestItem]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_TestItem]
      (
         [cTestItemId]    char(26)      COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cTestItemCode]  varchar(30)   COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cTestItemName]  varchar(100)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cTestItemQty]   int           NOT NULL,
         [cTestItemPrice] decimal(18,2) NOT NULL,
         [cTestItemState] int           NOT NULL,
         [cTestItemNote]  varchar(500)  COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [ustamp]         datetime      NOT NULL,
         [datestamp]      datetime      NOT NULL,
         [json_object]    nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_TestItem] PRIMARY KEY CLUSTERED ([cTestItemId]),
         CONSTRAINT [UQ_ta_TestItem_cTestItemCode] UNIQUE NONCLUSTERED ([cTestItemCode])
      );
   END;
GO
--endregion

--region ta_TestDoc
IF OBJECT_ID(N'[dbo].[ta_TestDoc]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_TestDoc]
      (
         [cTestDocId]        char(26)      COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cTestDocNo]        varchar(30)   COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cTestDocTitle]     varchar(100)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cTestDocAmount]    decimal(18,2) NOT NULL,
         -- 0 draft, 1 in approval, 2 approved, 3 rejected
         [cTestDocStatus]    int           NOT NULL,
         [cTestDocQaPassed]  bit           NULL,
         [cTestDocQaRemarks] varchar(500)  COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [ustamp]            datetime      NOT NULL,
         [datestamp]         datetime      NOT NULL,
         [json_object]       nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_TestDoc] PRIMARY KEY CLUSTERED ([cTestDocId]),
         CONSTRAINT [UQ_ta_TestDoc_cTestDocNo] UNIQUE NONCLUSTERED ([cTestDocNo])
      );
   END;
GO
--endregion

--region ta_Doc: the approval document types of the test module
IF NOT EXISTS (SELECT 1 FROM [dbo].[ta_Doc] WHERE [cDocName] = 'EmTestDoc')
   INSERT INTO [dbo].[ta_Doc] ([cDocName], [cDocDesc], [cDocAbv])
   VALUES ('EmTestDoc', 'Em Test Document (document approval)', 'TESTDOC');
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[ta_Doc] WHERE [cDocName] = 'EmTestItem')
   INSERT INTO [dbo].[ta_Doc] ([cDocName], [cDocDesc], [cDocAbv])
   VALUES ('EmTestItem', 'Em Test Item (data approval)', 'TESTITM');
GO
--endregion
