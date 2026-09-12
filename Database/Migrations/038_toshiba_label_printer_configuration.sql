-- Phase 4: structured label-printer configuration for Toshiba B-FV4D-GS14 LAN.

ALTER TABLE network_printers
    MODIFY COLUMN brand ENUM('epson', 'star', 'toshiba', 'other') NOT NULL DEFAULT 'epson',
    ADD COLUMN technology VARCHAR(50) NULL AFTER print_group_id,
    ADD COLUMN manufacturer VARCHAR(50) NULL AFTER technology,
    ADD COLUMN model_code VARCHAR(100) NULL AFTER manufacturer,
    ADD COLUMN protocol VARCHAR(30) NULL AFTER model_code,
    ADD COLUMN resolution_dpi INT NULL AFTER protocol,
    ADD COLUMN printing_method VARCHAR(50) NULL AFTER resolution_dpi,
    ADD COLUMN max_print_width_mm DECIMAL(7,2) NULL AFTER printing_method,
    ADD COLUMN supported_media VARCHAR(150) NULL AFTER max_print_width_mm,
    ADD COLUMN transport VARCHAR(50) NULL AFTER supported_media,
    ADD COLUMN windows_driver VARCHAR(150) NULL AFTER transport,
    ADD COLUMN label_profile VARCHAR(100) NULL AFTER windows_driver,
    ADD COLUMN media_width_mm DECIMAL(7,2) NULL AFTER label_profile,
    ADD COLUMN label_width_mm DECIMAL(7,2) NULL AFTER media_width_mm,
    ADD COLUMN label_height_mm DECIMAL(7,2) NULL AFTER label_width_mm,
    ADD COLUMN gap_size_mm DECIMAL(7,2) NULL AFTER label_height_mm,
    ADD COLUMN sensor_type VARCHAR(30) NULL AFTER gap_size_mm,
    ADD COLUMN print_speed INT NULL AFTER sensor_type,
    ADD COLUMN print_darkness INT NULL AFTER print_speed,
    ADD COLUMN horizontal_offset_mm DECIMAL(7,2) NOT NULL DEFAULT 0 AFTER print_darkness,
    ADD COLUMN vertical_offset_mm DECIMAL(7,2) NOT NULL DEFAULT 0 AFTER horizontal_offset_mm,
    ADD COLUMN finishing_mode VARCHAR(30) NOT NULL DEFAULT 'tearoff' AFTER vertical_offset_mm,
    ADD COLUMN number_of_copies INT NOT NULL DEFAULT 1 AFTER finishing_mode,
    ADD COLUMN is_default_label_printer TINYINT(1) NOT NULL DEFAULT 0 AFTER number_of_copies,
    ADD CONSTRAINT chk_label_copies CHECK (number_of_copies BETWEEN 1 AND 99),
    ADD CONSTRAINT chk_label_port CHECK (port BETWEEN 1 AND 65535),
    ADD CONSTRAINT chk_label_dimensions CHECK (
        printer_type <> 'label'
        OR (media_width_mm > 0 AND label_width_mm > 0 AND label_height_mm > 0
            AND label_width_mm <= media_width_mm AND label_width_mm <= 108)
    );

CREATE INDEX idx_network_printers_default_label
    ON network_printers(printer_type, is_default_label_printer, is_enabled);
