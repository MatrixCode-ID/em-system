-- Removes the product tables that tables/100-business.sql created before 2026-11-12: ta_ShoeHelper, ta_ProductTranData,
-- ta_ProductTran, ta_Product and ta_ProductType. Products now live in tables/120-product.sql.
-- The tables are dropped only when all of them are empty. When one holds rows, or a table outside this list refers to
-- one of them, the script stops with the reason and changes nothing. Safe to run again: missing tables are skipped.
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
   BEGIN TRANSACTION;

   -- Keep each table exclusively locked from its row check until the drop commits.
   DECLARE @legacy TABLE (name sysname NOT NULL, ord int NOT NULL);
   -- Children first, so every table goes together with its own foreign keys.
   INSERT INTO @legacy (name, ord) VALUES (N'ta_ShoeHelper', 1), (N'ta_ProductTranData', 2), (N'ta_ProductTran', 3), (N'ta_Product', 4), (N'ta_ProductType', 5);

   DECLARE @name sysname, @sql nvarchar(400), @rows bigint, @report nvarchar(1000) = N'', @held bit = 0;
   DECLARE legacy_rows CURSOR LOCAL FAST_FORWARD FOR
      SELECT name FROM @legacy WHERE OBJECT_ID(N'dbo.' + QUOTENAME(name), N'U') IS NOT NULL ORDER BY ord;
   OPEN legacy_rows;
   FETCH NEXT FROM legacy_rows INTO @name;
   WHILE @@FETCH_STATUS = 0
   BEGIN
      SET @sql = N'SELECT @rows = COUNT_BIG(*) FROM dbo.' + QUOTENAME(@name) + N' WITH (TABLOCKX, HOLDLOCK);';
      EXEC sp_executesql @sql, N'@rows bigint OUTPUT', @rows = @rows OUTPUT;
      SET @report = @report + CASE WHEN @report = N'' THEN N'' ELSE N', ' END + @name + N'=' + CAST(@rows AS nvarchar(20));
      IF @rows > 0 SET @held = 1;
      FETCH NEXT FROM legacy_rows INTO @name;
   END
   CLOSE legacy_rows;
   DEALLOCATE legacy_rows;

   IF @held = 1
   BEGIN
      SET @report = N'The legacy product tables hold rows and were left unchanged: ' + @report + N'.';
      THROW 51000, @report, 1;
   END

   DECLARE @outside nvarchar(400) = (
      SELECT TOP (1) fk.name + N' on ' + OBJECT_NAME(fk.parent_object_id)
      FROM sys.foreign_keys fk
      WHERE fk.referenced_object_id IN (SELECT OBJECT_ID(N'dbo.' + QUOTENAME(name), N'U') FROM @legacy)
        AND fk.parent_object_id NOT IN (SELECT ISNULL(OBJECT_ID(N'dbo.' + QUOTENAME(name), N'U'), 0) FROM @legacy));
   IF @outside IS NOT NULL
   BEGIN
      SET @report = N'The legacy product tables were left unchanged: another table still refers to them through ' + @outside + N'.';
      THROW 51000, @report, 1;
   END

   DECLARE legacy_drop CURSOR LOCAL FAST_FORWARD FOR
      SELECT name FROM @legacy WHERE OBJECT_ID(N'dbo.' + QUOTENAME(name), N'U') IS NOT NULL ORDER BY ord;
   OPEN legacy_drop;
   FETCH NEXT FROM legacy_drop INTO @name;
   WHILE @@FETCH_STATUS = 0
   BEGIN
      SET @sql = N'DROP TABLE dbo.' + QUOTENAME(@name) + N';';
      EXEC sp_executesql @sql;
      FETCH NEXT FROM legacy_drop INTO @name;
   END
   CLOSE legacy_drop;
   DEALLOCATE legacy_drop;

   COMMIT TRANSACTION;
END TRY
BEGIN CATCH
   IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
   THROW;
END CATCH
GO
