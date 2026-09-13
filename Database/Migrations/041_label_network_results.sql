-- Phase 9: persist distinct Toshiba network/print confirmation levels.

ALTER TABLE label_print_jobs
    ADD COLUMN network_reachable TINYINT(1) NULL AFTER error_message,
    ADD COLUMN port_reachable TINYINT(1) NULL AFTER network_reachable,
    ADD COLUMN protocol_confirmed TINYINT(1) NULL AFTER port_reachable,
    ADD COLUMN data_accepted TINYINT(1) NULL AFTER protocol_confirmed,
    ADD COLUMN physical_label_confirmed TINYINT(1) NOT NULL DEFAULT 0 AFTER data_accepted,
    ADD COLUMN printer_status_response VARBINARY(256) NULL AFTER physical_label_confirmed,
    ADD COLUMN transport_phase VARCHAR(30) NULL AFTER printer_status_response,
    ADD COLUMN transport_completed_at DATETIME NULL AFTER transport_phase,
    ADD COLUMN physical_confirmed_at DATETIME NULL AFTER transport_completed_at;

