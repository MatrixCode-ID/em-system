/* Attendance setup, MySQL 8.0+. Requires core, tenant and tenant organization tables. Re-runnable; no seed data.
   Work date means the date a shift starts; the end time of a shift is calculated, never stored. */
CREATE TABLE IF NOT EXISTS ta_TenantSchedTemplate (
 cTenantSchedTemplateId char(26) NOT NULL, cTenantId char(26) NOT NULL, cTenantSchedTemplateCode varchar(10) NOT NULL,
 cTenantSchedTemplateName varchar(255) NOT NULL, cTenantSchedTemplateStart time NOT NULL, cTenantSchedTemplateBreak int NOT NULL DEFAULT 0,
 cTenantSchedTemplateState int NOT NULL DEFAULT 0, cTenantSchedTemplateOrder int NOT NULL DEFAULT -1, cTenantSchedTemplateNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantSchedTemplateId), UNIQUE KEY UQ_ta_TenantSchedTemplate_cTenantId_cTenantSchedTemplateCode(cTenantId,cTenantSchedTemplateCode),
 UNIQUE KEY UQ_ta_TenantSchedTemplate_cTenantSchedTemplateId_cTenantId(cTenantSchedTemplateId,cTenantId),
 CONSTRAINT FK_ta_TenantSchedTemplate_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_TenantSchedTemplateDay (
 cTenantSchedTemplateDayId char(26) NOT NULL, cTenantSchedTemplateId char(26) NOT NULL, cTenantSchedTemplateDayWeekday int NOT NULL,
 cTenantSchedTemplateDayHours decimal(4,2) NOT NULL DEFAULT 0, cTenantSchedTemplateDayBreak int NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantSchedTemplateDayId),
 UNIQUE KEY UQ_ta_TenantSchedTemplateDay_Template_Weekday(cTenantSchedTemplateId,cTenantSchedTemplateDayWeekday),
 CONSTRAINT FK_ta_TenantSchedTemplateDay_ta_TenantSchedTemplate FOREIGN KEY(cTenantSchedTemplateId) REFERENCES ta_TenantSchedTemplate(cTenantSchedTemplateId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_TenantSchedSetting (
 cTenantSchedSettingId char(26) NOT NULL, cTenantId char(26) NOT NULL, cTenantDeptId char(26) NULL, cTenantSchedSettingKey char(26) NOT NULL,
 cTenantSchedSettingEnable int NOT NULL DEFAULT 0, cTenantSchedSettingMode int NOT NULL DEFAULT 0, cTenantSchedTemplateId char(26) NULL,
 cTenantSchedSettingStepWeeks int NULL, cTenantSchedSettingCycleStart date NULL, cTenantSchedSettingPeriodDays int NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantSchedSettingId), UNIQUE KEY UQ_ta_TenantSchedSetting_cTenantId_cTenantSchedSettingKey(cTenantId,cTenantSchedSettingKey),
 UNIQUE KEY UQ_ta_TenantSchedSetting_cTenantSchedSettingId_cTenantId(cTenantSchedSettingId,cTenantId),
 KEY IX_ta_TenantSchedSetting_cTenantDeptId(cTenantDeptId), KEY IX_ta_TenantSchedSetting_cTenantSchedTemplateId(cTenantSchedTemplateId),
 CONSTRAINT FK_ta_TenantSchedSetting_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId),
 CONSTRAINT FK_ta_TenantSchedSetting_ta_TenantDept FOREIGN KEY(cTenantDeptId,cTenantId) REFERENCES ta_TenantDept(cTenantDeptId,cTenantId),
 CONSTRAINT FK_ta_TenantSchedSetting_ta_TenantSchedTemplate FOREIGN KEY(cTenantSchedTemplateId,cTenantId) REFERENCES ta_TenantSchedTemplate(cTenantSchedTemplateId,cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_TenantSchedSettingStep (
 cTenantSchedSettingStepId char(26) NOT NULL, cTenantSchedSettingId char(26) NOT NULL, cTenantId char(26) NOT NULL,
 cTenantSchedSettingStepOrder int NOT NULL, cTenantSchedTemplateId char(26) NOT NULL, cTenantSchedSettingStepState int NOT NULL DEFAULT 0,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantSchedSettingStepId),
 UNIQUE KEY UQ_ta_TenantSchedSettingStep_Setting_Order(cTenantSchedSettingId,cTenantSchedSettingStepOrder),
 KEY IX_ta_TenantSchedSettingStep_cTenantSchedTemplateId(cTenantSchedTemplateId),
 CONSTRAINT FK_ta_TenantSchedSettingStep_ta_TenantSchedSetting FOREIGN KEY(cTenantSchedSettingId,cTenantId) REFERENCES ta_TenantSchedSetting(cTenantSchedSettingId,cTenantId),
 CONSTRAINT FK_ta_TenantSchedSettingStep_ta_TenantSchedTemplate FOREIGN KEY(cTenantSchedTemplateId,cTenantId) REFERENCES ta_TenantSchedTemplate(cTenantSchedTemplateId,cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_TenantAttCode (
 cTenantAttCodeId char(26) NOT NULL, cTenantId char(26) NOT NULL, cTenantAttCodeCode varchar(50) NOT NULL, cTenantAttCodeName varchar(255) NOT NULL,
 cTenantAttCodeCategory int NOT NULL, cTenantAttCodeState int NOT NULL DEFAULT 0, cTenantAttCodeOrder int NOT NULL DEFAULT -1, cTenantAttCodeNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantAttCodeId), UNIQUE KEY UQ_ta_TenantAttCode_cTenantId_cTenantAttCodeCode(cTenantId,cTenantAttCodeCode),
 CONSTRAINT FK_ta_TenantAttCode_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_PublicHoliday (
 cPublicHolidayId char(26) NOT NULL, cPublicHolidayName varchar(255) NOT NULL, cPublicHolidayKind int NOT NULL, cPublicHolidayMonth int NULL, cPublicHolidayDay int NULL,
 cPublicHolidayDate date NULL, cPublicHolidayCollective bit(1) NOT NULL DEFAULT b'0', cPublicHolidayState int NOT NULL DEFAULT 0, cPublicHolidayNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cPublicHolidayId), KEY IX_ta_PublicHoliday_cPublicHolidayDate(cPublicHolidayDate),
 KEY IX_ta_PublicHoliday_cPublicHolidayMonth_cPublicHolidayDay(cPublicHolidayMonth,cPublicHolidayDay)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
DROP TRIGGER IF EXISTS tr_ta_TenantSchedTemplate_ulid;
DROP TRIGGER IF EXISTS tr_ta_TenantSchedTemplateDay_ulid;
DROP TRIGGER IF EXISTS tr_ta_TenantSchedSetting_ulid;
DROP TRIGGER IF EXISTS tr_ta_TenantSchedSettingStep_ulid;
DROP TRIGGER IF EXISTS tr_ta_TenantAttCode_ulid;
DROP TRIGGER IF EXISTS tr_ta_PublicHoliday_ulid;
DELIMITER $$
CREATE TRIGGER tr_ta_TenantSchedTemplate_ulid BEFORE INSERT ON ta_TenantSchedTemplate FOR EACH ROW
BEGIN
 IF NEW.cTenantSchedTemplateId IS NULL OR NEW.cTenantSchedTemplateId = '' THEN SET NEW.cTenantSchedTemplateId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_TenantSchedTemplateDay_ulid BEFORE INSERT ON ta_TenantSchedTemplateDay FOR EACH ROW
BEGIN
 IF NEW.cTenantSchedTemplateDayId IS NULL OR NEW.cTenantSchedTemplateDayId = '' THEN SET NEW.cTenantSchedTemplateDayId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_TenantSchedSetting_ulid BEFORE INSERT ON ta_TenantSchedSetting FOR EACH ROW
BEGIN
 IF NEW.cTenantSchedSettingId IS NULL OR NEW.cTenantSchedSettingId = '' THEN SET NEW.cTenantSchedSettingId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_TenantSchedSettingStep_ulid BEFORE INSERT ON ta_TenantSchedSettingStep FOR EACH ROW
BEGIN
 IF NEW.cTenantSchedSettingStepId IS NULL OR NEW.cTenantSchedSettingStepId = '' THEN SET NEW.cTenantSchedSettingStepId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_TenantAttCode_ulid BEFORE INSERT ON ta_TenantAttCode FOR EACH ROW
BEGIN
 IF NEW.cTenantAttCodeId IS NULL OR NEW.cTenantAttCodeId = '' THEN SET NEW.cTenantAttCodeId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_PublicHoliday_ulid BEFORE INSERT ON ta_PublicHoliday FOR EACH ROW
BEGIN
 IF NEW.cPublicHolidayId IS NULL OR NEW.cPublicHolidayId = '' THEN SET NEW.cPublicHolidayId = NewUlidString(); END IF;
END$$
DELIMITER ;
