/*
    Tabel module uji Em.Test (set lengkap, jalankan sekali pada database inti).

    Jalankan pada database target (USE [NamaDatabase]). Aman dijalankan ulang: setiap tabel, view,
    dan baris jenis dokumen hanya dibuat kalau belum ada.

    Isi:
    - ta_TestItem / vi_TestItem : data induk sederhana untuk uji CRUD, paging, UiModel, dan data approval.
    - ta_TestDoc  / vi_TestDoc  : dokumen transaksi untuk uji document approval (PDF + stamp).
    - ta_Doc                    : dua jenis dokumen yang dipakai engine approval untuk module ini
                                  (EmTestDoc, EmTestItem). Tanpa baris ini request approval ditolak FK.

    Dokumentasi module: doc/engine-test-module.md
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

--region vi_TestItem
IF OBJECT_ID(N'[dbo].[vi_TestItem]', N'V') IS NULL
   EXEC (N'CREATE VIEW [dbo].[vi_TestItem] AS SELECT * FROM [dbo].[ta_TestItem]');
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
         -- 0 draf, 1 dalam approval, 2 disetujui, 3 ditolak
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

--region vi_TestDoc
IF OBJECT_ID(N'[dbo].[vi_TestDoc]', N'V') IS NULL
   EXEC (N'CREATE VIEW [dbo].[vi_TestDoc] AS SELECT * FROM [dbo].[ta_TestDoc]');
GO
--endregion

--region ta_Doc: jenis dokumen approval module uji
IF NOT EXISTS (SELECT 1 FROM [dbo].[ta_Doc] WHERE [cDocName] = 'EmTestDoc')
   INSERT INTO [dbo].[ta_Doc] ([cDocName], [cDocDesc], [cDocAbv])
   VALUES ('EmTestDoc', 'Em Test Document (document approval)', 'TESTDOC');
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[ta_Doc] WHERE [cDocName] = 'EmTestItem')
   INSERT INTO [dbo].[ta_Doc] ([cDocName], [cDocDesc], [cDocAbv])
   VALUES ('EmTestItem', 'Em Test Item (data approval)', 'TESTITM');
GO
--endregion
