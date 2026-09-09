namespace OrderWeb.Client.Services;

public static class ClientCacheSchema
{
    public static readonly string[] CreateStatements =
    {
        """
        CREATE TABLE IF NOT EXISTS device_config (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS mother_connection (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            mode TEXT NOT NULL,
            status TEXT NOT NULL,
            display_name TEXT,
            api_base_url TEXT,
            websocket_url TEXT,
            pairing_code_hint TEXT,
            terminal_id TEXT,
            auth_token_hint TEXT,
            last_seen_utc TEXT,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS restaurant_info (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            mother_id TEXT NOT NULL,
            name TEXT NOT NULL,
            description TEXT,
            currency TEXT NOT NULL,
            time_zone TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS sync_state (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS event_checkpoint (
            id INTEGER PRIMARY KEY,
            stream_name TEXT NOT NULL UNIQUE,
            last_event_id TEXT,
            last_event_utc TEXT,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS permissions_cache (
            id INTEGER PRIMARY KEY,
            user_id TEXT NOT NULL,
            permission_key TEXT NOT NULL,
            is_allowed INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL,
            UNIQUE (user_id, permission_key)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS current_session (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            user_id TEXT NOT NULL,
            user_name TEXT NOT NULL,
            role TEXT NOT NULL,
            session_token TEXT NOT NULL,
            expires_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS time_clock_sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id TEXT NOT NULL,
            user_name TEXT NOT NULL,
            role TEXT NOT NULL,
            clock_in_at TEXT NOT NULL,
            clock_out_at TEXT,
            terminal_in TEXT NOT NULL DEFAULT '',
            terminal_out TEXT,
            business_date TEXT NOT NULL,
            worked_minutes INTEGER,
            status TEXT NOT NULL DEFAULT 'open',
            synced_at TEXT,
            adjustment_note TEXT,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS categories (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            name TEXT NOT NULL,
            color TEXT,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1,
            parent_id INTEGER,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            category_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            description TEXT,
            sku TEXT,
            is_active INTEGER NOT NULL DEFAULT 1,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (category_id) REFERENCES categories(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS prices (
            id INTEGER PRIMARY KEY,
            product_id INTEGER NOT NULL,
            price_type TEXT NOT NULL DEFAULT 'standard',
            amount NUMERIC NOT NULL,
            currency TEXT NOT NULL DEFAULT 'GBP',
            tax_rate_id INTEGER,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (product_id) REFERENCES products(id),
            FOREIGN KEY (tax_rate_id) REFERENCES tax_rates(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS modifier_groups (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            name TEXT NOT NULL,
            min_select INTEGER NOT NULL DEFAULT 0,
            max_select INTEGER NOT NULL DEFAULT 1,
            is_active INTEGER NOT NULL DEFAULT 1,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS modifiers (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            modifier_group_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            price_delta NUMERIC NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (modifier_group_id) REFERENCES modifier_groups(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS product_modifiers (
            product_id INTEGER NOT NULL,
            modifier_group_id INTEGER NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL,
            PRIMARY KEY (product_id, modifier_group_id),
            FOREIGN KEY (product_id) REFERENCES products(id),
            FOREIGN KEY (modifier_group_id) REFERENCES modifier_groups(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS product_variants (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            product_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            description TEXT,
            takeaway_price NUMERIC NOT NULL DEFAULT 0,
            dine_in_price NUMERIC NOT NULL DEFAULT 0,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (product_id) REFERENCES products(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS product_quick_notes (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            product_id INTEGER NOT NULL,
            note_text TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (product_id) REFERENCES products(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS meal_deals (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            name TEXT NOT NULL,
            description TEXT,
            price NUMERIC NOT NULL DEFAULT 0,
            color TEXT,
            pick_count INTEGER NOT NULL DEFAULT 1,
            vat_category TEXT,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS meal_deal_choices (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            meal_deal_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (meal_deal_id) REFERENCES meal_deals(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS meal_deal_category_rules (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            meal_deal_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            is_required INTEGER NOT NULL DEFAULT 0,
            min_selections INTEGER NOT NULL DEFAULT 0,
            max_selections INTEGER NOT NULL DEFAULT 1,
            menu_item_mother_ids_json TEXT,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (meal_deal_id) REFERENCES meal_deals(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS tasting_menus (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            name TEXT NOT NULL,
            description TEXT,
            color TEXT,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS tasting_menu_options (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            tasting_menu_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            price NUMERIC NOT NULL DEFAULT 0,
            includes_wine INTEGER NOT NULL DEFAULT 0,
            course_count INTEGER NOT NULL DEFAULT 0,
            sort_order INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (tasting_menu_id) REFERENCES tasting_menus(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS tasting_menu_courses (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            tasting_menu_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            wine_name TEXT,
            course_number INTEGER NOT NULL DEFAULT 0,
            required INTEGER NOT NULL DEFAULT 1,
            vat_category TEXT,
            sort_order INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (tasting_menu_id) REFERENCES tasting_menus(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS tasting_menu_choices (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            course_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            print_group_id TEXT,
            sort_order INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (course_id) REFERENCES tasting_menu_courses(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS tax_rates (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            name TEXT NOT NULL,
            rate_percent NUMERIC NOT NULL,
            is_active INTEGER NOT NULL DEFAULT 1,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS floors (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            name TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1,
            background_image_id TEXT,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS tables (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            floor_id INTEGER NOT NULL,
            table_number TEXT NOT NULL,
            seats INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT 'available',
            current_total NUMERIC NOT NULL DEFAULT 0,
            current_order_id TEXT,
            covers INTEGER NOT NULL DEFAULT 0,
            server_name TEXT,
            session_status TEXT,
            minutes_occupied INTEGER NOT NULL DEFAULT 0,
            version INTEGER NOT NULL DEFAULT 1,
            position_x INTEGER NOT NULL DEFAULT 0,
            position_y INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (floor_id) REFERENCES floors(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS open_orders (
            id TEXT PRIMARY KEY,
            mother_id TEXT UNIQUE,
            order_number TEXT NOT NULL,
            order_type TEXT NOT NULL,
            table_id INTEGER,
            customer_id INTEGER,
            guests INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL,
            subtotal NUMERIC NOT NULL DEFAULT 0,
            tax NUMERIC NOT NULL DEFAULT 0,
            total NUMERIC NOT NULL DEFAULT 0,
            version INTEGER NOT NULL DEFAULT 1,
            opened_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (table_id) REFERENCES tables(id),
            FOREIGN KEY (customer_id) REFERENCES customers_cache(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS order_items (
            id TEXT PRIMARY KEY,
            order_id TEXT NOT NULL,
            product_id INTEGER,
            product_mother_id TEXT,
            name TEXT NOT NULL,
            quantity INTEGER NOT NULL,
            unit_price NUMERIC NOT NULL,
            notes TEXT,
            modifier_json TEXT,
            variant_id TEXT,
            variant_name TEXT,
            variant_price NUMERIC,
            meal_deal_id TEXT,
            meal_deal_choices_json TEXT,
            tasting_menu_id TEXT,
            status TEXT NOT NULL DEFAULT 'open',
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (order_id) REFERENCES open_orders(id),
            FOREIGN KEY (product_id) REFERENCES products(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS online_orders_cache (
            id TEXT PRIMARY KEY,
            mother_id TEXT UNIQUE,
            order_number TEXT NOT NULL,
            customer_name TEXT,
            order_type TEXT NOT NULL,
            due_time TEXT,
            status TEXT NOT NULL,
            total NUMERIC NOT NULL DEFAULT 0,
            payload_json TEXT,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS reservations_cache (
            id TEXT PRIMARY KEY,
            mother_id TEXT UNIQUE,
            customer_name TEXT,
            phone TEXT,
            table_id INTEGER,
            party_size INTEGER NOT NULL DEFAULT 0,
            reservation_utc TEXT NOT NULL,
            status TEXT NOT NULL,
            payload_json TEXT,
            updated_utc TEXT NOT NULL,
            FOREIGN KEY (table_id) REFERENCES tables(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS customers_cache (
            id INTEGER PRIMARY KEY,
            mother_id TEXT UNIQUE,
            name TEXT NOT NULL,
            phone TEXT,
            email TEXT,
            address TEXT,
            postcode TEXT,
            loyalty_points INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS pending_actions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            action_type TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            idempotency_key TEXT,
            status TEXT NOT NULL DEFAULT 'pending',
            retry_count INTEGER NOT NULL DEFAULT 0,
            last_error TEXT,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS print_requests (
            id TEXT PRIMARY KEY,
            print_type TEXT NOT NULL,
            order_id TEXT,
            status TEXT NOT NULL,
            message TEXT,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS sync_errors (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            area TEXT NOT NULL,
            error_code TEXT,
            message TEXT NOT NULL,
            payload_json TEXT,
            created_utc TEXT NOT NULL,
            resolved_utc TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS sync_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            sync_kind TEXT NOT NULL,
            status TEXT NOT NULL,
            bootstrap_id TEXT,
            payload_version INTEGER NOT NULL,
            checksum TEXT,
            message TEXT,
            started_utc TEXT NOT NULL,
            completed_utc TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS image_cache (
            image_id TEXT PRIMARY KEY,
            remote_path TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            local_path TEXT NOT NULL,
            mime_type TEXT,
            last_synchronized_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS order_history_day_snapshot (
            cache_key TEXT PRIMARY KEY,
            history_date TEXT NOT NULL,
            order_type TEXT NOT NULL,
            search TEXT NOT NULL,
            page INTEGER NOT NULL,
            page_size INTEGER NOT NULL,
            has_next_page INTEGER NOT NULL DEFAULT 0,
            message TEXT,
            payload_json TEXT NOT NULL,
            cached_utc TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_products_category_id ON products(category_id)",
        "CREATE INDEX IF NOT EXISTS idx_prices_product_id ON prices(product_id)",
        "CREATE INDEX IF NOT EXISTS idx_modifiers_group_id ON modifiers(modifier_group_id)",
        "CREATE INDEX IF NOT EXISTS idx_tables_floor_id ON tables(floor_id)",
        "CREATE INDEX IF NOT EXISTS idx_open_orders_status ON open_orders(status)",
        "CREATE INDEX IF NOT EXISTS idx_order_items_order_id ON order_items(order_id)",
        "CREATE INDEX IF NOT EXISTS idx_online_orders_status ON online_orders_cache(status)",
        "CREATE INDEX IF NOT EXISTS idx_customers_phone ON customers_cache(phone)",
        "CREATE INDEX IF NOT EXISTS idx_pending_actions_status ON pending_actions(status)",
        "CREATE INDEX IF NOT EXISTS idx_print_requests_status ON print_requests(status)",
        "CREATE INDEX IF NOT EXISTS idx_sync_errors_area ON sync_errors(area)",
        "CREATE INDEX IF NOT EXISTS idx_sync_history_started ON sync_history(started_utc)",
        "CREATE INDEX IF NOT EXISTS idx_image_cache_hash ON image_cache(content_hash)"
    };

    public static readonly string[] MigrationStatements =
    {
        "ALTER TABLE tables ADD COLUMN current_order_id TEXT",
        "ALTER TABLE tables ADD COLUMN covers INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE tables ADD COLUMN server_name TEXT",
        "ALTER TABLE tables ADD COLUMN session_status TEXT",
        "ALTER TABLE tables ADD COLUMN minutes_occupied INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE tables ADD COLUMN version INTEGER NOT NULL DEFAULT 1",
        "ALTER TABLE tables ADD COLUMN position_x INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE tables ADD COLUMN position_y INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE tables ADD COLUMN design_icon TEXT",
        "ALTER TABLE open_orders ADD COLUMN subtotal NUMERIC NOT NULL DEFAULT 0",
        "ALTER TABLE open_orders ADD COLUMN tax NUMERIC NOT NULL DEFAULT 0",
        "ALTER TABLE open_orders ADD COLUMN version INTEGER NOT NULL DEFAULT 1",
        "ALTER TABLE order_items ADD COLUMN modifier_json TEXT",
        "ALTER TABLE floors ADD COLUMN background_image_id TEXT",
        "ALTER TABLE order_items ADD COLUMN product_mother_id TEXT",
        "ALTER TABLE order_items ADD COLUMN variant_id TEXT",
        "ALTER TABLE order_items ADD COLUMN variant_name TEXT",
        "ALTER TABLE order_items ADD COLUMN variant_price NUMERIC",
        "ALTER TABLE order_items ADD COLUMN meal_deal_id TEXT",
        "ALTER TABLE order_items ADD COLUMN meal_deal_choices_json TEXT",
        "ALTER TABLE order_items ADD COLUMN tasting_menu_id TEXT",
        "ALTER TABLE categories ADD COLUMN parent_id INTEGER"
    };

    public static readonly string[] ResetTables =
    {
        "sync_errors",
        "print_requests",
        "pending_actions",
        "reservations_cache",
        "online_orders_cache",
        "order_history_day_snapshot",
        "order_items",
        "open_orders",
        "customers_cache",
        "tables",
        "floors",
        "product_modifiers",
        "modifiers",
        "modifier_groups",
        "prices",
        "products",
        "categories",
        "tax_rates",
        "permissions_cache",
        "current_session",
        "event_checkpoint",
        "mother_connection",
        "sync_state",
        "restaurant_info"
    };
}
