using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using MyFirstMauiApp.Models.FoodMenu;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Service for managing predefined comments (customer-facing) and notes (kitchen-only)
    /// </summary>
    public class CommentNoteService
    {
        private readonly string _connectionString;

        public CommentNoteService()
        {
            _connectionString = "Server=localhost;Database=Pos-net;User=root;Password=root;";
        }

        // ========================================
        // PREDEFINED COMMENTS (Customer-Facing)
        // ========================================

        /// <summary>
        /// Get all predefined comments
        /// </summary>
        public async Task<List<PredefinedComment>> GetAllCommentsAsync()
        {
            var comments = new List<PredefinedComment>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, CommentText, Category, DisplayOrder, Active, Color, CreatedAt, UpdatedAt
                    FROM PredefinedComments
                    ORDER BY Category ASC, DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    comments.Add(ParseComment(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting comments: {ex.Message}");
                throw;
            }

            return comments;
        }

        /// <summary>
        /// Get active comments only
        /// </summary>
        public async Task<List<PredefinedComment>> GetActiveCommentsAsync()
        {
            var comments = new List<PredefinedComment>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, CommentText, Category, DisplayOrder, Active, Color, CreatedAt, UpdatedAt
                    FROM PredefinedComments
                    WHERE Active = TRUE
                    ORDER BY Category ASC, DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    comments.Add(ParseComment(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting active comments: {ex.Message}");
                throw;
            }

            return comments;
        }

        /// <summary>
        /// Get comments by category
        /// </summary>
        public async Task<List<PredefinedComment>> GetCommentsByCategoryAsync(string category)
        {
            var comments = new List<PredefinedComment>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, CommentText, Category, DisplayOrder, Active, Color, CreatedAt, UpdatedAt
                    FROM PredefinedComments
                    WHERE Category = @Category
                    ORDER BY DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Category", category);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    comments.Add(ParseComment(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting comments by category: {ex.Message}");
                throw;
            }

            return comments;
        }

        /// <summary>
        /// Create a new comment
        /// </summary>
        public async Task<bool> CreateCommentAsync(PredefinedComment comment)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                if (string.IsNullOrEmpty(comment.Id))
                {
                    comment.Id = $"comment-{Guid.NewGuid()}";
                }

                var query = @"
                    INSERT INTO PredefinedComments 
                    (Id, CommentText, Category, DisplayOrder, Active, Color, CreatedAt, UpdatedAt)
                    VALUES 
                    (@Id, @CommentText, @Category, @DisplayOrder, @Active, @Color, @CreatedAt, @UpdatedAt)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", comment.Id);
                command.Parameters.AddWithValue("@CommentText", comment.CommentText);
                command.Parameters.AddWithValue("@Category", (object?)comment.Category ?? DBNull.Value);
                command.Parameters.AddWithValue("@DisplayOrder", comment.DisplayOrder);
                command.Parameters.AddWithValue("@Active", comment.Active);
                command.Parameters.AddWithValue("@Color", comment.Color);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating comment: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update a comment
        /// </summary>
        public async Task<bool> UpdateCommentAsync(PredefinedComment comment)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE PredefinedComments 
                    SET CommentText = @CommentText,
                        Category = @Category,
                        DisplayOrder = @DisplayOrder,
                        Active = @Active,
                        Color = @Color,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", comment.Id);
                command.Parameters.AddWithValue("@CommentText", comment.CommentText);
                command.Parameters.AddWithValue("@Category", (object?)comment.Category ?? DBNull.Value);
                command.Parameters.AddWithValue("@DisplayOrder", comment.DisplayOrder);
                command.Parameters.AddWithValue("@Active", comment.Active);
                command.Parameters.AddWithValue("@Color", comment.Color);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating comment: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete a comment
        /// </summary>
        public async Task<bool> DeleteCommentAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = "DELETE FROM PredefinedComments WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting comment: {ex.Message}");
                throw;
            }
        }

        // ========================================
        // PREDEFINED NOTES (Kitchen-Only)
        // ========================================

        /// <summary>
        /// Get all predefined notes
        /// </summary>
        public async Task<List<PredefinedNote>> GetAllNotesAsync()
        {
            var notes = new List<PredefinedNote>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, NoteText, Category, Priority, DisplayOrder, Active, Color, CreatedAt, UpdatedAt
                    FROM PredefinedNotes
                    ORDER BY 
                        CASE Priority
                            WHEN 'urgent' THEN 1
                            WHEN 'high' THEN 2
                            WHEN 'normal' THEN 3
                            WHEN 'low' THEN 4
                            ELSE 5
                        END,
                        Category ASC, 
                        DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    notes.Add(ParseNote(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting notes: {ex.Message}");
                throw;
            }

            return notes;
        }

        /// <summary>
        /// Get active notes only
        /// </summary>
        public async Task<List<PredefinedNote>> GetActiveNotesAsync()
        {
            var notes = new List<PredefinedNote>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, NoteText, Category, Priority, DisplayOrder, Active, Color, CreatedAt, UpdatedAt
                    FROM PredefinedNotes
                    WHERE Active = TRUE
                    ORDER BY 
                        CASE Priority
                            WHEN 'urgent' THEN 1
                            WHEN 'high' THEN 2
                            WHEN 'normal' THEN 3
                            WHEN 'low' THEN 4
                            ELSE 5
                        END,
                        Category ASC, 
                        DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    notes.Add(ParseNote(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting active notes: {ex.Message}");
                throw;
            }

            return notes;
        }

        /// <summary>
        /// Get notes by priority
        /// </summary>
        public async Task<List<PredefinedNote>> GetNotesByPriorityAsync(string priority)
        {
            var notes = new List<PredefinedNote>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, NoteText, Category, Priority, DisplayOrder, Active, Color, CreatedAt, UpdatedAt
                    FROM PredefinedNotes
                    WHERE Priority = @Priority
                    ORDER BY DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Priority", priority);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    notes.Add(ParseNote(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting notes by priority: {ex.Message}");
                throw;
            }

            return notes;
        }

        /// <summary>
        /// Create a new note
        /// </summary>
        public async Task<bool> CreateNoteAsync(PredefinedNote note)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                if (string.IsNullOrEmpty(note.Id))
                {
                    note.Id = $"note-{Guid.NewGuid()}";
                }

                var query = @"
                    INSERT INTO PredefinedNotes 
                    (Id, NoteText, Category, Priority, DisplayOrder, Active, Color, CreatedAt, UpdatedAt)
                    VALUES 
                    (@Id, @NoteText, @Category, @Priority, @DisplayOrder, @Active, @Color, @CreatedAt, @UpdatedAt)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", note.Id);
                command.Parameters.AddWithValue("@NoteText", note.NoteText);
                command.Parameters.AddWithValue("@Category", (object?)note.Category ?? DBNull.Value);
                command.Parameters.AddWithValue("@Priority", note.Priority);
                command.Parameters.AddWithValue("@DisplayOrder", note.DisplayOrder);
                command.Parameters.AddWithValue("@Active", note.Active);
                command.Parameters.AddWithValue("@Color", note.Color);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating note: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update a note
        /// </summary>
        public async Task<bool> UpdateNoteAsync(PredefinedNote note)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE PredefinedNotes 
                    SET NoteText = @NoteText,
                        Category = @Category,
                        Priority = @Priority,
                        DisplayOrder = @DisplayOrder,
                        Active = @Active,
                        Color = @Color,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", note.Id);
                command.Parameters.AddWithValue("@NoteText", note.NoteText);
                command.Parameters.AddWithValue("@Category", (object?)note.Category ?? DBNull.Value);
                command.Parameters.AddWithValue("@Priority", note.Priority);
                command.Parameters.AddWithValue("@DisplayOrder", note.DisplayOrder);
                command.Parameters.AddWithValue("@Active", note.Active);
                command.Parameters.AddWithValue("@Color", note.Color);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating note: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete a note
        /// </summary>
        public async Task<bool> DeleteNoteAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = "DELETE FROM PredefinedNotes WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting note: {ex.Message}");
                throw;
            }
        }

        // ========================================
        // ITEM-COMMENT RELATIONSHIPS
        // ========================================

        /// <summary>
        /// Get comments linked to a specific menu item
        /// </summary>
        public async Task<List<PredefinedComment>> GetCommentsForItemAsync(string menuItemId)
        {
            var comments = new List<PredefinedComment>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT c.Id, c.CommentText, c.Category, c.DisplayOrder, c.Active, c.Color, c.CreatedAt, c.UpdatedAt
                    FROM PredefinedComments c
                    INNER JOIN ItemComments ic ON c.Id = ic.CommentId
                    WHERE ic.MenuItemId = @MenuItemId
                    ORDER BY c.Category ASC, c.DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@MenuItemId", menuItemId);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    comments.Add(ParseComment(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting comments for item: {ex.Message}");
                throw;
            }

            return comments;
        }

        /// <summary>
        /// Link comments to a menu item
        /// </summary>
        public async Task<bool> LinkCommentsToItemAsync(string menuItemId, List<string> commentIds)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Delete existing links
                    var deleteQuery = "DELETE FROM ItemComments WHERE MenuItemId = @MenuItemId";
                    using (var deleteCommand = new MySqlCommand(deleteQuery, connection, transaction))
                    {
                        deleteCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                        await deleteCommand.ExecuteNonQueryAsync();
                    }

                    // Insert new links
                    foreach (var commentId in commentIds)
                    {
                        var insertQuery = @"
                            INSERT INTO ItemComments (Id, MenuItemId, CommentId, CreatedAt)
                            VALUES (@Id, @MenuItemId, @CommentId, @CreatedAt)";

                        using var insertCommand = new MySqlCommand(insertQuery, connection, transaction);
                        insertCommand.Parameters.AddWithValue("@Id", $"ic-{Guid.NewGuid()}");
                        insertCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                        insertCommand.Parameters.AddWithValue("@CommentId", commentId);
                        insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                        await insertCommand.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error linking comments to item: {ex.Message}");
                throw;
            }
        }

        // Helper methods

        private PredefinedComment ParseComment(MySqlDataReader reader)
        {
            return new PredefinedComment
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                CommentText = reader.GetString(reader.GetOrdinal("CommentText")),
                Category = reader.IsDBNull(reader.GetOrdinal("Category")) ? null : reader.GetString(reader.GetOrdinal("Category")),
                DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                Color = reader.GetString(reader.GetOrdinal("Color")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };
        }

        private PredefinedNote ParseNote(MySqlDataReader reader)
        {
            return new PredefinedNote
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                NoteText = reader.GetString(reader.GetOrdinal("NoteText")),
                Category = reader.IsDBNull(reader.GetOrdinal("Category")) ? null : reader.GetString(reader.GetOrdinal("Category")),
                Priority = reader.GetString(reader.GetOrdinal("Priority")),
                DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                Color = reader.GetString(reader.GetOrdinal("Color")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };
        }
    }
}
