-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_TenantAttCode AS
SELECT c.* FROM ta_TenantAttCode c;
