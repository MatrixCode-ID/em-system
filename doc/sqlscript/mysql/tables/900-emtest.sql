/* MySQL 8.0+ application schema. Install after its core dependencies. */

CREATE TABLE IF NOT EXISTS `ta_TestItem`
      (
         `cTestItemId`    char(26)      COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cTestItemCode`  varchar(30)   COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cTestItemName`  varchar(100)  COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cTestItemQty`   int           NOT NULL,
         `cTestItemPrice` decimal(18,2) NOT NULL,
         `cTestItemState` int           NOT NULL,
         `cTestItemNote`  varchar(500)  COLLATE utf8mb4_0900_ai_ci NULL,
         `ustamp`         datetime(3)      NOT NULL,
         `datestamp`      datetime(3)      NOT NULL,
         `json_object`    longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_TestItem` PRIMARY KEY  (`cTestItemId`),
         CONSTRAINT `UQ_ta_TestItem_cTestItemCode` UNIQUE  (`cTestItemCode`)
      ,
   `cTestDocId` char(26) NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_TestDoc`
      (
         `cTestDocId`        char(26)      COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cTestDocNo`        varchar(30)   COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cTestDocTitle`     varchar(100)  COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cTestDocAmount`    decimal(18,2) NOT NULL,
         -- 0 draft, 1 in approval, 2 approved, 3 rejected
         `cTestDocStatus`    int           NOT NULL,
         `cTestDocQaPassed`  bit           NULL,
         `cTestDocQaRemarks` varchar(500)  COLLATE utf8mb4_0900_ai_ci NULL,
         `ustamp`            datetime(3)      NOT NULL,
         `datestamp`         datetime(3)      NOT NULL,
         `json_object`       longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_TestDoc` PRIMARY KEY  (`cTestDocId`),
         CONSTRAINT `UQ_ta_TestDoc_cTestDocNo` UNIQUE  (`cTestDocNo`)
      
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS mysql_prepare_test_link;
DELIMITER $$
CREATE PROCEDURE mysql_prepare_test_link()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_TestItem' AND COLUMN_NAME='cTestDocId') THEN
  ALTER TABLE ta_TestItem ADD cTestDocId char(26) NULL;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='FK_TestItem_TestDoc') THEN
  ALTER TABLE ta_TestItem ADD CONSTRAINT FK_TestItem_TestDoc FOREIGN KEY(cTestDocId) REFERENCES ta_TestDoc(cTestDocId);
 END IF;
END$$
DELIMITER ;
CALL mysql_prepare_test_link();
DROP PROCEDURE mysql_prepare_test_link;
