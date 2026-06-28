-- OrderWeb POS — production database user setup (REFERENCE ONLY)
--
-- Production installs must NOT run this file manually.
-- Use OrderWeb.DatabaseSetup.exe install-mother instead:
--
--   OrderWeb.DatabaseSetup.exe install-mother ^
--     --root-user root --root-password %MARIA_ROOT_PWD%
--
-- That command will:
--   1. Generate a random password for orderweb_app
--   2. Create orderweb_pos + orderweb_app@localhost/127.0.0.1/192.168.%
--   3. Write C:\ProgramData\OrderWebPOS\orderweb-database.json (no root/root)
--   4. Run migrations and verify schema
--
-- This SQL file is kept for documentation and emergency manual recovery only.

-- ---------------------------------------------------------------------------
-- Option A — New production database name (recommended for fresh installs)
-- ---------------------------------------------------------------------------
CREATE DATABASE IF NOT EXISTS orderweb_pos
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

-- BEFORE running manually: replace CHANGE_ME_STRONG_PASSWORD with a strong password (20+ chars).

CREATE USER IF NOT EXISTS 'orderweb_app'@'localhost' IDENTIFIED BY 'CHANGE_ME_STRONG_PASSWORD';
CREATE USER IF NOT EXISTS 'orderweb_app'@'127.0.0.1' IDENTIFIED BY 'CHANGE_ME_STRONG_PASSWORD';
CREATE USER IF NOT EXISTS 'orderweb_app'@'192.168.%' IDENTIFIED BY 'CHANGE_ME_STRONG_PASSWORD';

GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES, CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE, CREATE VIEW, SHOW VIEW, TRIGGER
  ON orderweb_pos.*
  TO 'orderweb_app'@'localhost';

GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES, CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE, CREATE VIEW, SHOW VIEW, TRIGGER
  ON orderweb_pos.*
  TO 'orderweb_app'@'127.0.0.1';

GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES, CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE, CREATE VIEW, SHOW VIEW, TRIGGER
  ON orderweb_pos.*
  TO 'orderweb_app'@'192.168.%';

FLUSH PRIVILEGES;

-- Verify (run manually):
--   SHOW GRANTS FOR 'orderweb_app'@'localhost';
--   mysql -u orderweb_app -p -h 127.0.0.1 orderweb_pos -e "SELECT 1;"

-- If user already existed and you only need to rotate password:
--   ALTER USER 'orderweb_app'@'localhost' IDENTIFIED BY 'NEW_PASSWORD';
--   ALTER USER 'orderweb_app'@'127.0.0.1' IDENTIFIED BY 'NEW_PASSWORD';
--   ALTER USER 'orderweb_app'@'192.168.%' IDENTIFIED BY 'NEW_PASSWORD';
--   FLUSH PRIVILEGES;
