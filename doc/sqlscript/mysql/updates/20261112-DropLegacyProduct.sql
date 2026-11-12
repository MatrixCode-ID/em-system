/* Removes the product tables that tables/100-business.sql created before 2026-11-12: ta_ShoeHelper, ta_ProductTranData,
   ta_ProductTran, ta_Product and ta_ProductType. Products now live in tables/120-product.sql.
   The tables are dropped only when all of them are empty. When one holds rows, or a table outside this list refers to
   one of them, the script stops with the reason and changes nothing. Safe to run again: missing tables are skipped.
   Pause writes to the legacy tables during this update because MySQL DDL commits implicitly. */
DROP PROCEDURE IF EXISTS mysql_drop_legacy_product;
DELIMITER $$
CREATE PROCEDURE mysql_drop_legacy_product()
BEGIN
 DECLARE legacy_name varchar(64);
 DECLARE report varchar(600) DEFAULT '';
 DECLARE message varchar(128);
 DECLARE outside varchar(200);
 DECLARE held int DEFAULT 0;
 DECLARE done int DEFAULT 0;
 -- Children first, so every table goes together with its own foreign keys.
 DECLARE legacy_rows CURSOR FOR
  SELECT l.name FROM (SELECT 'ta_ShoeHelper' AS name, 1 AS ord UNION ALL SELECT 'ta_ProductTranData', 2 UNION ALL SELECT 'ta_ProductTran', 3
   UNION ALL SELECT 'ta_Product', 4 UNION ALL SELECT 'ta_ProductType', 5) l
  JOIN information_schema.TABLES t ON t.TABLE_SCHEMA=DATABASE() AND t.TABLE_NAME=l.name ORDER BY l.ord;
 DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;
 OPEN legacy_rows;
 count_rows: LOOP
  FETCH legacy_rows INTO legacy_name;
  IF done = 1 THEN LEAVE count_rows; END IF;
  SET @legacy_count = 0;
  SET @legacy_sql = CONCAT('SELECT COUNT(*) INTO @legacy_count FROM `', legacy_name, '`');
  PREPARE statement FROM @legacy_sql; EXECUTE statement; DEALLOCATE PREPARE statement;
  SET report = CONCAT(report, IF(report = '', '', ', '), legacy_name, '=', @legacy_count);
  IF @legacy_count > 0 THEN SET held = 1; END IF;
 END LOOP;
 CLOSE legacy_rows;
 IF held = 1 THEN
  SET message = LEFT(CONCAT('Legacy product tables hold rows: ', report), 128);
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = message;
 END IF;
 SET outside = (SELECT CONCAT(CONSTRAINT_NAME, ' on ', TABLE_NAME) FROM information_schema.REFERENTIAL_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA=DATABASE() AND REFERENCED_TABLE_NAME IN ('ta_ShoeHelper','ta_ProductTranData','ta_ProductTran','ta_Product','ta_ProductType')
   AND TABLE_NAME NOT IN ('ta_ShoeHelper','ta_ProductTranData','ta_ProductTran','ta_Product','ta_ProductType') LIMIT 1);
 IF outside IS NOT NULL THEN
  SET message = LEFT(CONCAT('Legacy product tables are still referenced: ', outside), 128);
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = message;
 END IF;
 DROP TABLE IF EXISTS ta_ShoeHelper, ta_ProductTranData, ta_ProductTran, ta_Product, ta_ProductType;
END$$
DELIMITER ;
CALL mysql_drop_legacy_product();
DROP PROCEDURE mysql_drop_legacy_product;
