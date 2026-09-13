-- Brother TD-4420DN label profiles. No Brother driver file is stored.

INSERT INTO label_media_profiles
    (id, profile_name, manufacturer, model_code, width_mm, height_mm, gap_mm, sensor_type, darkness, speed_ips,
     horizontal_offset_mm, vertical_offset_mm, finishing_mode, is_active)
VALUES
    ('brother-60x40-container', 'Brother 60 × 40 mm Container', 'brother', 'td-4420dn', 60, 40, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
    ('brother-51x30-compact', 'Brother 51 × 30 mm Compact', 'brother', 'td-4420dn', 51, 30, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
    ('brother-80x50-delivery', 'Brother 80 × 50 mm Delivery', 'brother', 'td-4420dn', 80, 50, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1)
ON DUPLICATE KEY UPDATE
    manufacturer = VALUES(manufacturer),
    model_code = VALUES(model_code),
    updated_at = CURRENT_TIMESTAMP;
