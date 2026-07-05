-- 017: Add an explicit menu item type for reliable food/drink best-seller reporting.

ALTER TABLE FoodMenuItems
    ADD COLUMN IF NOT EXISTS ItemType VARCHAR(20) NOT NULL DEFAULT 'Food' AFTER calculated_vat_rate;

CREATE INDEX IF NOT EXISTS idx_foodmenu_item_type ON FoodMenuItems (ItemType);

UPDATE FoodMenuItems
SET ItemType = 'Drink'
WHERE COALESCE(ItemType, 'Food') = 'Food'
  AND LOWER(CONCAT(COALESCE(Name, ''), ' ', COALESCE(Description, ''))) REGEXP
      'drink|beverage|juice|coffee|tea|water|soda|cola|milkshake|shake|smoothie|latte|espresso|cappuccino|hot chocolate|beer|wine|cider|mocktail|cocktail|soft drink|bottle|bottled';

ALTER TABLE MenuItems
    ADD COLUMN IF NOT EXISTS ItemType VARCHAR(20) NOT NULL DEFAULT 'Food' AFTER Active;

CREATE INDEX IF NOT EXISTS idx_menu_items_type ON MenuItems (ItemType);

UPDATE MenuItems
SET ItemType = 'Drink'
WHERE COALESCE(ItemType, 'Food') = 'Food'
  AND LOWER(CONCAT(COALESCE(Name, ''), ' ', COALESCE(Description, ''))) REGEXP
      'drink|beverage|juice|coffee|tea|water|soda|cola|milkshake|shake|smoothie|latte|espresso|cappuccino|hot chocolate|beer|wine|cider|mocktail|cocktail|soft drink|bottle|bottled';
