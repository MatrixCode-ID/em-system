/* Rename old registry robot objects without deleting data. MySQL DDL implicitly commits;
   back up first. Preflight all conflicts before any rename. Requires MySQL 8.0+. */
DROP PROCEDURE IF EXISTS mysql_upgrade_robot_names;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_robot_names()
BEGIN
 DECLARE done int DEFAULT 0;
 DECLARE tbl varchar(64); DECLARE oldcol varchar(64); DECLARE newcol varchar(64);
 DECLARE checkname varchar(64); DECLARE checkexpr longtext; DECLARE enforced varchar(3);
 DECLARE cur CURSOR FOR SELECT t,o,n FROM mysql_robot_rename_map;
 DECLARE checks_cur CURSOR FOR SELECT n,expr,enf FROM mysql_robot_checks;
 DECLARE CONTINUE HANDLER FOR NOT FOUND SET done=1;
 DROP TEMPORARY TABLE IF EXISTS mysql_robot_rename_map;
 CREATE TEMPORARY TABLE mysql_robot_rename_map(t varchar(64),o varchar(64),n varchar(64));
 INSERT INTO mysql_robot_rename_map VALUES
 ('ta_Robot','cCtnRobotId','cRobotId'),('ta_Robot','cCtnRobotName','cRobotName'),
 ('ta_Robot','cCtnRobotState','cRobotState'),('ta_Robot','cCtnRobotDescription','cRobotDescription'),
 ('ta_Robot','cCtnRobotTokenHash','cRobotTokenHash'),('ta_Robot','cCtnRobotTokenPrefix','cRobotTokenPrefix'),
 ('ta_Robot','cCtnRobotTokenExpiry','cRobotTokenExpiry'),('ta_Robot','cCtnRobotTokenLastUsed','cRobotTokenLastUsed'),
 ('ta_CtnRootRobot','cCtnRobotId','cRobotId'),('ta_CtnUpload','cCtnRobotId','cRobotId'),
 ('ta_CtnManifest','cCtnManifestPushedBy_cCtnRobotId','cCtnManifestPushedBy_cRobotId'),
 ('ta_CtnRootRobot','cCtnRobotRootAccess','cCtnRootRobotAccess');
 IF EXISTS(SELECT 1 FROM information_schema.TABLES a JOIN information_schema.TABLES b ON b.TABLE_SCHEMA=a.TABLE_SCHEMA
   WHERE a.TABLE_SCHEMA=DATABASE() AND ((a.TABLE_NAME='ta_CtnRobot' AND b.TABLE_NAME='ta_Robot') OR (a.TABLE_NAME='ta_CtnRobotRoot' AND b.TABLE_NAME='ta_CtnRootRobot'))) THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Both old and new robot tables exist; reconcile first';
 END IF;
 IF EXISTS(SELECT 1 FROM mysql_robot_rename_map m JOIN information_schema.COLUMNS a ON a.TABLE_SCHEMA=DATABASE()
  AND a.TABLE_NAME=CASE m.t WHEN 'ta_Robot' THEN IF(EXISTS(SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_CtnRobot'),'ta_CtnRobot',m.t)
    WHEN 'ta_CtnRootRobot' THEN IF(EXISTS(SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_CtnRobotRoot'),'ta_CtnRobotRoot',m.t) ELSE m.t END AND a.COLUMN_NAME=m.o
  JOIN information_schema.COLUMNS b ON b.TABLE_SCHEMA=a.TABLE_SCHEMA AND b.TABLE_NAME=a.TABLE_NAME AND b.COLUMN_NAME=m.n) THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Both old and new robot columns exist; reconcile first';
 END IF;
 IF EXISTS(SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_CtnRobot') THEN
  RENAME TABLE ta_CtnRobot TO ta_Robot;
 END IF;
 IF EXISTS(SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_CtnRobotRoot') THEN
  RENAME TABLE ta_CtnRobotRoot TO ta_CtnRootRobot;
 END IF;
 DROP TEMPORARY TABLE IF EXISTS mysql_robot_checks;
 CREATE TEMPORARY TABLE mysql_robot_checks(n varchar(64),expr longtext,enf varchar(3));
 INSERT INTO mysql_robot_checks
 SELECT tc.CONSTRAINT_NAME,cc.CHECK_CLAUSE,tc.ENFORCED FROM information_schema.TABLE_CONSTRAINTS tc
 JOIN information_schema.CHECK_CONSTRAINTS cc ON cc.CONSTRAINT_SCHEMA=tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME=tc.CONSTRAINT_NAME
 WHERE tc.CONSTRAINT_SCHEMA=DATABASE() AND tc.TABLE_NAME='ta_CtnRootRobot' AND tc.CONSTRAINT_TYPE='CHECK'
 AND LOWER(cc.CHECK_CLAUSE) LIKE '%cctnrobotrootaccess%';
 OPEN checks_cur;
 drop_checks: LOOP
  FETCH checks_cur INTO checkname,checkexpr,enforced; IF done=1 THEN LEAVE drop_checks; END IF;
  SET @mysql_upgrade_sql=CONCAT('ALTER TABLE `ta_CtnRootRobot` DROP CHECK `',REPLACE(checkname,'`','``'),'`');
  PREPARE stmt FROM @mysql_upgrade_sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
 END LOOP;
 CLOSE checks_cur; SET done=0;
 OPEN cur;
 rename_loop: LOOP
  FETCH cur INTO tbl,oldcol,newcol;
  IF done=1 THEN LEAVE rename_loop; END IF;
  IF EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=tbl AND COLUMN_NAME=oldcol) THEN
   SET @mysql_upgrade_sql=CONCAT('ALTER TABLE `',REPLACE(tbl,'`','``'),'` RENAME COLUMN `',REPLACE(oldcol,'`','``'),'` TO `',REPLACE(newcol,'`','``'),'`');
   PREPARE stmt FROM @mysql_upgrade_sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END IF;
 END LOOP;
 CLOSE cur; SET done=0;
 OPEN checks_cur;
 restore_checks: LOOP
  FETCH checks_cur INTO checkname,checkexpr,enforced; IF done=1 THEN LEAVE restore_checks; END IF;
  SET @mysql_upgrade_sql=CONCAT('ALTER TABLE `ta_CtnRootRobot` ADD CONSTRAINT `',REPLACE(checkname,'`','``'),'` CHECK (',REPLACE(REPLACE(checkexpr,CONCAT(CHAR(92),CHAR(39)),CHAR(39)),'cCtnRobotRootAccess','cCtnRootRobotAccess'),')',IF(enforced='NO',' NOT ENFORCED',''));
  PREPARE stmt FROM @mysql_upgrade_sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
 END LOOP;
 CLOSE checks_cur;
 DROP TEMPORARY TABLE mysql_robot_checks;
 DROP TEMPORARY TABLE mysql_robot_rename_map;
END$$
DELIMITER ;
CALL mysql_upgrade_robot_names();
DROP PROCEDURE mysql_upgrade_robot_names;
