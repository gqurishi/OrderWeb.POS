-- Phase 7: explicit component-mode behaviour for item labels.

ALTER TABLE FoodMenuItems
    ADD COLUMN also_print_main_label TINYINT(1) NOT NULL DEFAULT 0 AFTER print_component_labels;
