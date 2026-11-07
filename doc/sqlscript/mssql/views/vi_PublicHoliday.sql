/* Public holiday read model. Requires tables/110-attendance.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_PublicHoliday;
GO
CREATE VIEW dbo.vi_PublicHoliday AS
SELECT h.* FROM dbo.ta_PublicHoliday h;
GO
SELECT TOP 100 * FROM dbo.vi_PublicHoliday;
GO
