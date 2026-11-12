/* Adds the update and creation stamps to the unit table of an installation that already has tables/100-business.sql,
   so a unit can be edited with an update-stamp check. Existing rows get the time this script runs. Safe to run again.
   Run before tables/120-product.sql and views/vi_Unit.sql. */
DROP PROCEDURE IF EXISTS mysql_upgrade_unit_stamp;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_unit_stamp()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_Unit') THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Install tables/100-business.sql first';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_Unit' AND COLUMN_NAME='ustamp') THEN
  ALTER TABLE ta_Unit ADD ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_Unit' AND COLUMN_NAME='datestamp') THEN
  ALTER TABLE ta_Unit ADD datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3);
 END IF;
END$$
DELIMITER ;
CALL mysql_upgrade_unit_stamp();
DROP PROCEDURE mysql_upgrade_unit_stamp;
