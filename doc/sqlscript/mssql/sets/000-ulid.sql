/*
    Native SQL Server ULID functions (the complete set, run once on a new database).

    Run on the target database (USE [DatabaseName]) before the scripts that need it.
    The objects are ordered by dependency: _UlidBase32Value, _UlidEntropy, NewUlid,
    GetUlidRandomPart(String), GetUlidDatePart(String), NewUlidStringFromBytes, NewUlidString.
*/

SET XACT_ABORT ON;
GO

--region _UlidBase32Value
DROP FUNCTION IF EXISTS [dbo].[_UlidBase32Value];
GO

CREATE FUNCTION [dbo].[_UlidBase32Value](@value TINYINT)
   RETURNS TINYINT
AS
BEGIN
   DECLARE @result TINYINT;

   IF @value BETWEEN 48 AND 57
      BEGIN
         SET @result = @value - 48;
         RETURN @result;
      END;

   IF @value BETWEEN 97 AND 122
      BEGIN
         SET @value = @value - 32;
      END;

   SET @result =
           CASE @value
              WHEN 65 THEN 10
              WHEN 66 THEN 11
              WHEN 67 THEN 12
              WHEN 68 THEN 13
              WHEN 69 THEN 14
              WHEN 70 THEN 15
              WHEN 71 THEN 16
              WHEN 72 THEN 17
              WHEN 74 THEN 18
              WHEN 75 THEN 19
              WHEN 77 THEN 20
              WHEN 78 THEN 21
              WHEN 80 THEN 22
              WHEN 81 THEN 23
              WHEN 82 THEN 24
              WHEN 83 THEN 25
              WHEN 84 THEN 26
              WHEN 86 THEN 27
              WHEN 87 THEN 28
              WHEN 88 THEN 29
              WHEN 89 THEN 30
              WHEN 90 THEN 31
              ELSE NULL
              END;

   RETURN @result;
END;
GO
--endregion

--region _UlidEntropy
DROP VIEW IF EXISTS [dbo].[_UlidEntropy];
GO

CREATE VIEW [dbo].[_UlidEntropy]
AS
SELECT CONVERT(BINARY(10), CRYPT_GEN_RANDOM(10)) AS [RandomPart];
GO
--endregion

--region NewUlid
DROP FUNCTION IF EXISTS [dbo].[NewUlid];
GO

CREATE FUNCTION [dbo].[NewUlid]()
   RETURNS BINARY(16)
AS
BEGIN
   DECLARE @milliseconds BIGINT =
      DATEDIFF_BIG(
              MILLISECOND,
              CONVERT(DATETIME2(3), '19700101', 112),
              SYSUTCDATETIME()
      );
   DECLARE @timestamp BINARY(6) =
      SUBSTRING(CONVERT(BINARY(8), @milliseconds), 3, 6);
   DECLARE @random BINARY(10);

   SELECT
      @random = [RandomPart]
   FROM [dbo].[_UlidEntropy];

   RETURN CONVERT(BINARY(16), @timestamp + @random);
END;
GO
--endregion

--region GetUlidRandomPart
DROP FUNCTION IF EXISTS [dbo].[GetUlidRandomPart];
GO

CREATE FUNCTION [dbo].[GetUlidRandomPart](@ulid BINARY(16))
   RETURNS BINARY(10)
AS
BEGIN
   IF @ulid IS NULL OR DATALENGTH(@ulid) <> 16
      BEGIN
         RETURN NULL;
      END;

   RETURN CONVERT(BINARY(10), SUBSTRING(@ulid, 7, 10));
END;
GO
--endregion

--region GetUlidRandomPartString
DROP FUNCTION IF EXISTS [dbo].[GetUlidRandomPartString];
GO

CREATE FUNCTION [dbo].[GetUlidRandomPartString](@ulid CHAR(26))
   RETURNS BINARY(10)
AS
BEGIN
   IF @ulid IS NULL OR LEN(@ulid) <> 26
      BEGIN
         RETURN NULL;
      END;

   DECLARE @i INT = 1;
   DECLARE @value TINYINT;

   WHILE @i <= 26
      BEGIN
         SET @value =
                 [dbo].[_UlidBase32Value](
                         ASCII(SUBSTRING(@ulid, @i, 1))
                 );

         IF @value IS NULL OR (@i = 1 AND @value > 7)
            BEGIN
               RETURN NULL;
            END;

         SET @i += 1;
      END;

   DECLARE @v10 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 11, 1)));
   DECLARE @v11 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 12, 1)));
   DECLARE @v12 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 13, 1)));
   DECLARE @v13 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 14, 1)));
   DECLARE @v14 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 15, 1)));
   DECLARE @v15 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 16, 1)));
   DECLARE @v16 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 17, 1)));
   DECLARE @v17 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 18, 1)));
   DECLARE @v18 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 19, 1)));
   DECLARE @v19 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 20, 1)));
   DECLARE @v20 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 21, 1)));
   DECLARE @v21 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 22, 1)));
   DECLARE @v22 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 23, 1)));
   DECLARE @v23 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 24, 1)));
   DECLARE @v24 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 25, 1)));
   DECLARE @v25 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 26, 1)));

   RETURN
      CONVERT(BINARY(1), (@v10 * 8) + (@v11 / 4)) +
      CONVERT(BINARY(1), ((@v11 % 4) * 64) + (@v12 * 2) + (@v13 / 16)) +
      CONVERT(BINARY(1), ((@v13 % 16) * 16) + (@v14 / 2)) +
      CONVERT(BINARY(1), ((@v14 % 2) * 128) + (@v15 * 4) + (@v16 / 8)) +
      CONVERT(BINARY(1), ((@v16 % 8) * 32) + @v17) +
      CONVERT(BINARY(1), (@v18 * 8) + (@v19 / 4)) +
      CONVERT(BINARY(1), ((@v19 % 4) * 64) + (@v20 * 2) + (@v21 / 16)) +
      CONVERT(BINARY(1), ((@v21 % 16) * 16) + (@v22 / 2)) +
      CONVERT(BINARY(1), ((@v22 % 2) * 128) + (@v23 * 4) + (@v24 / 8)) +
      CONVERT(BINARY(1), ((@v24 % 8) * 32) + @v25);
END;
GO
--endregion

--region GetUlidDatePart
DROP FUNCTION IF EXISTS [dbo].[GetUlidDatePart];
GO

CREATE FUNCTION [dbo].[GetUlidDatePart](@ulid BINARY(16))
   RETURNS DATETIMEOFFSET(3)
AS
BEGIN
   IF @ulid IS NULL OR DATALENGTH(@ulid) <> 16
      BEGIN
         RETURN NULL;
      END;

   DECLARE @timestampBytes BINARY(8) =
      0x0000 + CONVERT(BINARY(6), SUBSTRING(@ulid, 1, 6));
   DECLARE @milliseconds BIGINT = CONVERT(BIGINT, @timestampBytes);

   IF @milliseconds < 0 OR @milliseconds > 253402300799999
      BEGIN
         RETURN NULL;
      END;

   DECLARE @dayCount INT = CONVERT(INT, @milliseconds / 86400000);
   DECLARE @millisecondInDay INT = CONVERT(INT, @milliseconds % 86400000);
   DECLARE @dateTime DATETIME2(3) =
      DATEADD(
              MILLISECOND,
              @millisecondInDay,
              DATEADD(
                      DAY,
                      @dayCount,
                      CONVERT(DATETIME2(3), '19700101', 112)
              )
      );

   RETURN TODATETIMEOFFSET(@dateTime, '+00:00');
END;
GO
--endregion

--region GetUlidDatePartString
DROP FUNCTION IF EXISTS [dbo].[GetUlidDatePartString];
GO

CREATE FUNCTION [dbo].[GetUlidDatePartString](@ulid CHAR(26))
   RETURNS DATETIMEOFFSET(3)
AS
BEGIN
   IF @ulid IS NULL OR LEN(@ulid) <> 26
      BEGIN
         RETURN NULL;
      END;

   DECLARE @i INT = 1;
   DECLARE @value TINYINT;

   WHILE @i <= 26
      BEGIN
         SET @value =
                 [dbo].[_UlidBase32Value](
                         ASCII(SUBSTRING(@ulid, @i, 1))
                 );

         IF @value IS NULL OR (@i = 1 AND @value > 7)
            BEGIN
               RETURN NULL;
            END;

         SET @i += 1;
      END;

   DECLARE @v0 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 1, 1)));
   DECLARE @v1 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 2, 1)));
   DECLARE @v2 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 3, 1)));
   DECLARE @v3 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 4, 1)));
   DECLARE @v4 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 5, 1)));
   DECLARE @v5 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 6, 1)));
   DECLARE @v6 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 7, 1)));
   DECLARE @v7 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 8, 1)));
   DECLARE @v8 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 9, 1)));
   DECLARE @v9 TINYINT =
      [dbo].[_UlidBase32Value](ASCII(SUBSTRING(@ulid, 10, 1)));

   DECLARE @timestampBytes BINARY(8) =
      0x0000 +
      CONVERT(BINARY(1), (@v0 * 32) + @v1) +
      CONVERT(BINARY(1), (@v2 * 8) + (@v3 / 4)) +
      CONVERT(BINARY(1), ((@v3 % 4) * 64) + (@v4 * 2) + (@v5 / 16)) +
      CONVERT(BINARY(1), ((@v5 % 16) * 16) + (@v6 / 2)) +
      CONVERT(BINARY(1), ((@v6 % 2) * 128) + (@v7 * 4) + (@v8 / 8)) +
      CONVERT(BINARY(1), ((@v8 % 8) * 32) + @v9);
   DECLARE @milliseconds BIGINT = CONVERT(BIGINT, @timestampBytes);

   IF @milliseconds < 0 OR @milliseconds > 253402300799999
      BEGIN
         RETURN NULL;
      END;

   DECLARE @dayCount INT = CONVERT(INT, @milliseconds / 86400000);
   DECLARE @millisecondInDay INT = CONVERT(INT, @milliseconds % 86400000);
   DECLARE @dateTime DATETIME2(3) =
      DATEADD(
              MILLISECOND,
              @millisecondInDay,
              DATEADD(
                      DAY,
                      @dayCount,
                      CONVERT(DATETIME2(3), '19700101', 112)
              )
      );

   RETURN TODATETIMEOFFSET(@dateTime, '+00:00');
END;
GO
--endregion

--region NewUlidStringFromBytes
DROP FUNCTION IF EXISTS [dbo].[NewUlidStringFromBytes];
GO

CREATE FUNCTION [dbo].[NewUlidStringFromBytes](@ulid BINARY(16))
   RETURNS CHAR(26)
AS
BEGIN
   IF @ulid IS NULL OR DATALENGTH(@ulid) <> 16
      BEGIN
         RETURN NULL;
      END;

   DECLARE @alphabet CHAR(32) = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
   DECLARE @b0 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 1, 1));
   DECLARE @b1 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 2, 1));
   DECLARE @b2 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 3, 1));
   DECLARE @b3 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 4, 1));
   DECLARE @b4 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 5, 1));
   DECLARE @b5 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 6, 1));
   DECLARE @b6 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 7, 1));
   DECLARE @b7 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 8, 1));
   DECLARE @b8 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 9, 1));
   DECLARE @b9 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 10, 1));
   DECLARE @b10 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 11, 1));
   DECLARE @b11 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 12, 1));
   DECLARE @b12 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 13, 1));
   DECLARE @b13 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 14, 1));
   DECLARE @b14 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 15, 1));
   DECLARE @b15 INT = CONVERT(TINYINT, SUBSTRING(@ulid, 16, 1));

   RETURN
      SUBSTRING(@alphabet, (@b0 / 32) + 1, 1) +
      SUBSTRING(@alphabet, (@b0 % 32) + 1, 1) +
      SUBSTRING(@alphabet, (@b1 / 8) + 1, 1) +
      SUBSTRING(@alphabet, ((@b1 % 8) * 4) + (@b2 / 64) + 1, 1) +
      SUBSTRING(@alphabet, ((@b2 % 64) / 2) + 1, 1) +
      SUBSTRING(@alphabet, ((@b2 % 2) * 16) + (@b3 / 16) + 1, 1) +
      SUBSTRING(@alphabet, ((@b3 % 16) * 2) + (@b4 / 128) + 1, 1) +
      SUBSTRING(@alphabet, ((@b4 % 128) / 4) + 1, 1) +
      SUBSTRING(@alphabet, ((@b4 % 4) * 8) + (@b5 / 32) + 1, 1) +
      SUBSTRING(@alphabet, (@b5 % 32) + 1, 1) +
      SUBSTRING(@alphabet, (@b6 / 8) + 1, 1) +
      SUBSTRING(@alphabet, ((@b6 % 8) * 4) + (@b7 / 64) + 1, 1) +
      SUBSTRING(@alphabet, ((@b7 % 64) / 2) + 1, 1) +
      SUBSTRING(@alphabet, ((@b7 % 2) * 16) + (@b8 / 16) + 1, 1) +
      SUBSTRING(@alphabet, ((@b8 % 16) * 2) + (@b9 / 128) + 1, 1) +
      SUBSTRING(@alphabet, ((@b9 % 128) / 4) + 1, 1) +
      SUBSTRING(@alphabet, ((@b9 % 4) * 8) + (@b10 / 32) + 1, 1) +
      SUBSTRING(@alphabet, (@b10 % 32) + 1, 1) +
      SUBSTRING(@alphabet, (@b11 / 8) + 1, 1) +
      SUBSTRING(@alphabet, ((@b11 % 8) * 4) + (@b12 / 64) + 1, 1) +
      SUBSTRING(@alphabet, ((@b12 % 64) / 2) + 1, 1) +
      SUBSTRING(@alphabet, ((@b12 % 2) * 16) + (@b13 / 16) + 1, 1) +
      SUBSTRING(@alphabet, ((@b13 % 16) * 2) + (@b14 / 128) + 1, 1) +
      SUBSTRING(@alphabet, ((@b14 % 128) / 4) + 1, 1) +
      SUBSTRING(@alphabet, ((@b14 % 4) * 8) + (@b15 / 32) + 1, 1) +
      SUBSTRING(@alphabet, (@b15 % 32) + 1, 1);
END;
GO
--endregion

--region NewUlidString
DROP FUNCTION IF EXISTS [dbo].[NewUlidString];
GO

CREATE FUNCTION [dbo].[NewUlidString]()
   RETURNS CHAR(26)
AS
BEGIN
   RETURN [dbo].[NewUlidStringFromBytes]([dbo].[NewUlid]());
END;
GO
--endregion

