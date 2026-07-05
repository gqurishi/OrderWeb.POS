-- 015: Align item quick notes with the app model.
-- Current code stores per-item quick notes directly as NoteText.

ALTER TABLE MenuItemQuickNotes
    ADD COLUMN IF NOT EXISTS NoteText VARCHAR(255) NULL AFTER MenuItemId;

SET @menu_quick_notes_has_note_id = (
    SELECT COUNT(*)
    FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'MenuItemQuickNotes'
      AND column_name = 'NoteId'
);

SET @menu_quick_notes_copy_sql = IF(
    @menu_quick_notes_has_note_id > 0,
    'UPDATE MenuItemQuickNotes mqn
     INNER JOIN PredefinedNotes pn ON pn.Id = mqn.NoteId
     SET mqn.NoteText = pn.NoteText
     WHERE mqn.NoteText IS NULL',
    'SELECT 1'
);

PREPARE menu_quick_notes_copy_stmt FROM @menu_quick_notes_copy_sql;
EXECUTE menu_quick_notes_copy_stmt;
DEALLOCATE PREPARE menu_quick_notes_copy_stmt;

UPDATE MenuItemQuickNotes
SET NoteText = ''
WHERE NoteText IS NULL;

ALTER TABLE MenuItemQuickNotes
    MODIFY COLUMN NoteText VARCHAR(255) NOT NULL;

ALTER TABLE MenuItemQuickNotes
    DROP FOREIGN KEY IF EXISTS fk_quick_notes_note;

DROP INDEX IF EXISTS uq_menu_item_note ON MenuItemQuickNotes;
DROP INDEX IF EXISTS idx_quick_notes_note ON MenuItemQuickNotes;

ALTER TABLE MenuItemQuickNotes
    DROP COLUMN IF EXISTS NoteId;

CREATE INDEX IF NOT EXISTS idx_menuitem_quick_note_active ON MenuItemQuickNotes (Active);
CREATE INDEX IF NOT EXISTS idx_menuitem_quick_note_order ON MenuItemQuickNotes (DisplayOrder);
