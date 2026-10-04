/*
    020 - Tabel engine approval: jenis dokumen (ta_Doc) dan tujuh tabel request approval.

    Jalankan pada database target (USE [NamaDatabase]) sesudah tables/010-core.sql (foreign key
    penanda tangan, peminta, dan atas-nama merujuk ta_User). Bagian 1 ditujukan untuk database baru;
    bagian 2 dan 3 aman dijalankan ulang (setiap tabel, index, dan foreign key dibuat hanya kalau
    belum ada).

    Baris jenis dokumen di ta_Doc TIDAK dibuat di sini: diisi aplikasi/modul pemakai (mis.
    tables/900-emtest.sql). Request approval untuk jenis dokumen yang belum terdaftar ditolak FK.

    Dua hal di bawah ini bagian dari PERILAKU engine, bukan optimasi:
      - unique index tersaring (jenis dokumen, kunci, versi) WHERE Stage = 1, yang menjadi penjaga
        balapan dua pengajuan berbarengan - engine mengandalkannya, bukan hanya memeriksa di kode;
      - foreign key penanda tangan/peminta/atas-nama ke ta_User, yang membuat akun sistem (admin
        bawaan dan debugger, id-nya bukan ULID sah) gagal saat mencoba menandatangani.

    Panduan: doc/engine/engine-approval.md.
*/

/* Wajib untuk filtered index; sqlcmd menjalankan batch dengan QUOTED_IDENTIFIER
   OFF, dan tanpa ini pembuatan index gagal dengan "SET options have incorrect
   settings". */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ============================================================================
-- 1. Jenis dokumen (ta_Doc)
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

-- Singkatan dokumen menjadi awalan Id setiap dokumen, jadi dua jenis dokumen dengan singkatan
-- yang sama tidak bisa dibedakan lagi.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'UX_ta_Doc_cDocAbv'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_Doc]'))
BEGIN
   IF EXISTS (SELECT [cDocAbv] FROM [dbo].[ta_Doc] GROUP BY [cDocAbv] HAVING COUNT(*) > 1)
   BEGIN
      -- cDocAbv bukan kolom unicode, dan STRING_AGG menolak pemisah unicode untuknya.
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
-- 2. Tabel request approval
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1. ta_ApprovalRequest - header request
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
-- 2. ta_ApprovalRequestStep - satu baris per langkah
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
-- 3. ta_ApprovalRequestStepSigner - penanda tangan yang ditetapkan saat submit
--    Ramping dengan sengaja: tanpa kolom standar, kunci gabungan ketiga kolom.
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
-- 4. ta_ApprovalRequestItem - entitas yang diusulkan berubah
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
-- 5. ta_ApprovalRequestItemKey - bagian-bagian kunci entitas
--    Ramping: tanpa kolom standar, kunci gabungan entitas + nama bagian.
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
-- 6. ta_ApprovalRequestItemField - kolom yang diusulkan berubah
--    Ramping: tanpa kolom standar, kunci gabungan entitas + nama kolom.
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
-- 7. ta_ApprovalRequestComment - komentar bebas
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
-- 8. Index
-- ----------------------------------------------------------------------------

/* Penjaga balapan dua pengajuan berbarengan untuk dokumen + versi yang sama.
   Bagian dari perilaku engine: tanpa index ini dua request bisa sama-sama lolos
   pemeriksaan di kode lalu dua-duanya tersimpan sebagai menunggu. */
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

-- Seluruh request satu dokumen, termasuk yang sudah selesai dan yang ditarik.
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

-- Daftar request ber-paging, disaring tahap lalu jenis dokumen.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ta_ApprovalRequest_Stage'
                  AND [object_id] = OBJECT_ID(N'[dbo].[ta_ApprovalRequest]'))
BEGIN
   CREATE NONCLUSTERED INDEX [IX_ta_ApprovalRequest_Stage]
      ON [dbo].[ta_ApprovalRequest] ([cApprovalRequestStage], [cApprovalRequestDocType], [cApprovalRequestDate]);
   PRINT N'Created IX_ta_ApprovalRequest_Stage.';
END
GO

-- Request yang diajukan seorang user.
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

-- Langkah-langkah satu request, dan langkah di level yang sedang berjalan.
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

/* Daftar pekerjaan: langkah yang masih menunggu, dicocokkan dengan claim yang
   dipegang user. Tersaring ke yang menunggu saja, karena hanya baris itu yang
   pernah dicari lewat jalan ini. */
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

-- Tanda tangan seorang user, untuk riwayat dan untuk pemeriksaan empat mata.
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

-- Daftar pekerjaan: langkah yang penanda tangannya ditetapkan per orang.
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

-- Entitas usulan satu request, dalam urutan penerapannya.
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

/* Riwayat usulan atas satu entitas - dipakai saat melihat siapa pernah mengubah
   apa pada sebuah baris data. */
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

-- Komentar satu request, terurut waktu.
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
-- 9. Foreign key
--
--    Ke ta_User: NO ACTION dengan sengaja. User tidak pernah dibuang (hanya
--    ditandai tidak aktif), dan tanda tangan tidak boleh ikut hilang kalau
--    kelak ada yang mencoba membuangnya. FK inilah yang membuat akun sistem
--    gagal saat mencoba menandatangani - lihat catatan di kepala berkas.
--
--    Ke request/entitas induk: CASCADE, supaya membuang satu request membuang
--    seluruh isinya dan tidak meninggalkan baris menggantung.
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
