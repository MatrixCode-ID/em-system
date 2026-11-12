-- Adds the update and creation stamps to the unit table of an installation that already has tables/100-business.sql,
-- so a unit can be edited with an update-stamp check. Existing rows get the time this script runs. Safe to run again.
-- Run before tables/120-product.sql and views/vi_Unit.sql.
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.ta_Unit', N'U') IS NULL
   THROW 51000, 'Install tables/100-business.sql before this update.', 1;
GO
IF COL_LENGTH(N'dbo.ta_Unit', N'ustamp') IS NULL
   ALTER TABLE [dbo].[ta_Unit] ADD [ustamp] datetime NOT NULL CONSTRAINT [DF_ta_Unit_ustamp] DEFAULT (getdate());
GO
IF COL_LENGTH(N'dbo.ta_Unit', N'datestamp') IS NULL
   ALTER TABLE [dbo].[ta_Unit] ADD [datestamp] datetime NOT NULL CONSTRAINT [DF_ta_Unit_datestamp] DEFAULT (getdate());
GO
