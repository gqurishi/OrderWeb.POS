-- Xprinter XP-421B label profiles. No Xprinter driver file is stored.

INSERT INTO label_media_profiles
    (id, profile_name, manufacturer, model_code, width_mm, height_mm, gap_mm, sensor_type, darkness, speed_ips,
     horizontal_offset_mm, vertical_offset_mm, finishing_mode, is_active)
VALUES
    ('xprinter-60x40-container', 'Xprinter 60 × 40 mm Container', 'xprinter', 'xp-421b', 60, 40, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
    ('xprinter-51x30-compact', 'Xprinter 51 × 30 mm Compact', 'xprinter', 'xp-421b', 51, 30, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
    ('xprinter-80x50-delivery', 'Xprinter 80 × 50 mm Delivery', 'xprinter', 'xp-421b', 80, 50, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1)
ON DUPLICATE KEY UPDATE
    manufacturer = VALUES(manufacturer),
    model_code = VALUES(model_code),
    updated_at = CURRENT_TIMESTAMP;
