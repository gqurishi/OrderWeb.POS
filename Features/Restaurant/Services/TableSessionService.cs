using MySqlConnector;
using POS_in_NET.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace POS_in_NET.Services
{
    /// <summary>
    /// Service for managing table sessions and enhanced restaurant operations
    /// </summary>
    public class TableSessionService
    {
        private readonly DatabaseService _databaseService;
        private bool? _isCurrentOrderIdNumeric;
        private bool? _canStoreCurrentOrderReference;
        private bool _sessionSchemaMaintenanceChecked;

        public TableSessionService()
        {
            _databaseService = new DatabaseService();
        }

        /// <summary>
        /// Open a new table session (seat customers)
        /// </summary>
        public async Task<(bool success, string message)> OpenTableAsync(int tableId, int partySize, string? customerNotes = null, string? specialOccasion = null)
        {
            var result = await OpenTableWithSessionAsync(tableId, partySize, customerNotes, specialOccasion);
            return (result.success, result.message);
        }

        public async Task<(bool success, string message, int? sessionId)> OpenTableWithSessionAsync(int tableId, int partySize, string? customerNotes = null, string? specialOccasion = null)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();

                string currentStatus;
                int? currentSessionId;

                using (var checkCommand = new MySqlCommand("SELECT Status, CurrentSessionId FROM RestaurantTables WHERE Id = @tableId", connection, transaction))
                {
                    checkCommand.Parameters.AddWithValue("@tableId", tableId);
                    using var reader = await checkCommand.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        await transaction.RollbackAsync();
                        return (false, "Table not found", null);
                    }

                    currentStatus = reader.GetString(reader.GetOrdinal("Status"));
                    currentSessionId = reader.IsDBNull(reader.GetOrdinal("CurrentSessionId"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("CurrentSessionId"));
                }

                if (currentSessionId.HasValue)
                {
                    var existingSession = await GetSessionByIdAsync(connection, transaction, currentSessionId.Value);
                    if (existingSession != null && existingSession.IsActive)
                    {
                        using var normalizeTable = new MySqlCommand(@"
                            UPDATE RestaurantTables
                            SET Status = 'Occupied',
                                CurrentSessionId = @sessionId,
                                UpdatedDate = CURRENT_TIMESTAMP
                            WHERE Id = @tableId", connection, transaction);
                        normalizeTable.Parameters.AddWithValue("@sessionId", currentSessionId.Value);
                        normalizeTable.Parameters.AddWithValue("@tableId", tableId);
                        await normalizeTable.ExecuteNonQueryAsync();

                        await transaction.CommitAsync();
                        return (true, "Existing active session resumed", currentSessionId.Value);
                    }

                    using var clearStaleSession = new MySqlCommand(@"
                        UPDATE RestaurantTables
                        SET Status = 'Available',
                            CurrentSessionId = NULL,
                            UpdatedDate = CURRENT_TIMESTAMP
                        WHERE Id = @tableId", connection, transaction);
                    clearStaleSession.Parameters.AddWithValue("@tableId", tableId);
                    await clearStaleSession.ExecuteNonQueryAsync();

                    currentStatus = "Available";
                }

                if (!string.Equals(currentStatus, "Available", StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return (false, "Table is not available", null);
                }

                var sessionNumber = await GenerateSessionNumberAsync(connection, transaction);

                int createdSessionId;
                using (var insertCommand = new MySqlCommand(@"
                    INSERT INTO TableSessions (TableId, SessionNumber, PartySize, Status, CustomerNotes, SpecialOccasion)
                    VALUES (@tableId, @sessionNumber, @partySize, 'Occupied', @customerNotes, @specialOccasion)", connection, transaction))
                {
                    insertCommand.Parameters.AddWithValue("@tableId", tableId);
                    insertCommand.Parameters.AddWithValue("@sessionNumber", sessionNumber);
                    insertCommand.Parameters.AddWithValue("@partySize", partySize);
                    insertCommand.Parameters.AddWithValue("@customerNotes", customerNotes ?? "");
                    insertCommand.Parameters.AddWithValue("@specialOccasion", specialOccasion);

                    await insertCommand.ExecuteNonQueryAsync();
                    createdSessionId = Convert.ToInt32(insertCommand.LastInsertedId);
                }

                using (var updateTable = new MySqlCommand(@"
                    UPDATE RestaurantTables
                    SET Status = 'Occupied',
                        CurrentSessionId = @sessionId,
                        LastOccupied = CURRENT_TIMESTAMP,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @tableId", connection, transaction))
                {
                    updateTable.Parameters.AddWithValue("@sessionId", createdSessionId);
                    updateTable.Parameters.AddWithValue("@tableId", tableId);
                    await updateTable.ExecuteNonQueryAsync();
                }

                await InsertSessionEventAsync(connection, transaction, createdSessionId, "opened", "system", JsonSerializer.Serialize(new
                {
                    tableId,
                    partySize
                }));

                await transaction.CommitAsync();
                return (true, $"Table opened successfully for {partySize} guests (Session: {sessionNumber})", createdSessionId);
            }
            catch (Exception ex)
            {
                return (false, $"Error opening table: {ex.Message}", null);
            }
        }

        public async Task<(int repairedCount, string message)> BackfillOpenTableSessionsAsync()
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);

                var candidates = new List<(string OrderId, string CustomerName)>();
                using (var command = new MySqlCommand(@"
                    SELECT order_id, customer_name
                    FROM orders
                    WHERE COALESCE(source_channel, 'local') = 'local'
                      AND COALESCE(order_type, 'table') = 'table'
                      AND COALESCE(is_open, 1) = 1
                      AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')
                      AND (table_session_id IS NULL OR table_session_id = 0)
                    ORDER BY updated_at DESC, id DESC", connection))
                using (var reader = (MySqlDataReader)await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var orderId = reader["order_id"]?.ToString();
                        var customerName = reader["customer_name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(orderId) && !string.IsNullOrWhiteSpace(customerName))
                        {
                            candidates.Add((orderId, customerName));
                        }
                    }
                }

                if (candidates.Count == 0)
                {
                    return (0, "No open table orders require backfill");
                }

                var repaired = 0;
                var processedTableNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in candidates)
                {
                    var tableNumber = ParseTableNumberFromCustomerName(candidate.CustomerName);
                    if (string.IsNullOrWhiteSpace(tableNumber))
                    {
                        continue;
                    }

                    if (processedTableNumbers.Contains(tableNumber))
                    {
                        continue;
                    }

                    using var tx = await connection.BeginTransactionAsync();
                    try
                    {
                        int? tableId;
                        using (var tableCommand = new MySqlCommand(@"
                            SELECT Id
                            FROM RestaurantTables
                            WHERE TableNumber = @tableNumber
                              AND IsActive = TRUE
                            LIMIT 1", connection, tx))
                        {
                            tableCommand.Parameters.AddWithValue("@tableNumber", tableNumber);
                            var tableScalar = await tableCommand.ExecuteScalarAsync();
                            tableId = tableScalar == null || tableScalar == DBNull.Value
                                ? null
                                : Convert.ToInt32(tableScalar);
                        }

                        if (!tableId.HasValue)
                        {
                            await tx.RollbackAsync();
                            continue;
                        }

                        var activeSession = await GetActiveSessionByTableIdAsync(connection, tx, tableId.Value);
                        var sessionId = activeSession?.Id;
                        if (!sessionId.HasValue)
                        {
                            var sessionNumber = await GenerateSessionNumberAsync(connection, tx);
                            using var createSession = new MySqlCommand(@"
                                INSERT INTO TableSessions (TableId, SessionNumber, PartySize, Status, CustomerNotes, SpecialOccasion)
                                VALUES (@tableId, @sessionNumber, @partySize, 'Ordering', @customerNotes, NULL)", connection, tx);
                            createSession.Parameters.AddWithValue("@tableId", tableId.Value);
                            createSession.Parameters.AddWithValue("@sessionNumber", sessionNumber);
                            createSession.Parameters.AddWithValue("@partySize", 1);
                            createSession.Parameters.AddWithValue("@customerNotes", "Auto-recovered from open table order");
                            await createSession.ExecuteNonQueryAsync();
                            sessionId = Convert.ToInt32(createSession.LastInsertedId);
                        }

                        var orderReference = await ResolveSessionOrderReferenceAsync(connection, tx, candidate.OrderId);

                        var updateSessionSql = orderReference != null
                            ? @"
                            UPDATE TableSessions
                            SET CurrentOrderId = @orderReference,
                                Status = CASE WHEN Status = 'Occupied' THEN 'Ordering' ELSE Status END,
                                UpdatedDate = CURRENT_TIMESTAMP
                            WHERE Id = @sessionId"
                            : @"
                            UPDATE TableSessions
                            SET Status = CASE WHEN Status = 'Occupied' THEN 'Ordering' ELSE Status END,
                                UpdatedDate = CURRENT_TIMESTAMP
                            WHERE Id = @sessionId";
                        using (var updateSession = new MySqlCommand(updateSessionSql, connection, tx))
                        {
                            if (orderReference != null)
                            {
                                updateSession.Parameters.AddWithValue("@orderReference", orderReference);
                            }
                            updateSession.Parameters.AddWithValue("@sessionId", sessionId.Value);
                            await updateSession.ExecuteNonQueryAsync();
                        }

                        using (var updateTable = new MySqlCommand(@"
                            UPDATE RestaurantTables
                            SET Status = 'Occupied',
                                CurrentSessionId = @sessionId,
                                LastOccupied = CURRENT_TIMESTAMP,
                                UpdatedDate = CURRENT_TIMESTAMP
                            WHERE Id = @tableId", connection, tx))
                        {
                            updateTable.Parameters.AddWithValue("@sessionId", sessionId.Value);
                            updateTable.Parameters.AddWithValue("@tableId", tableId.Value);
                            await updateTable.ExecuteNonQueryAsync();
                        }

                        using (var updateOrder = new MySqlCommand(@"
                            UPDATE orders
                            SET table_session_id = @sessionId,
                                updated_at = CURRENT_TIMESTAMP
                            WHERE order_id = @orderId", connection, tx))
                        {
                            updateOrder.Parameters.AddWithValue("@sessionId", sessionId.Value);
                            updateOrder.Parameters.AddWithValue("@orderId", candidate.OrderId);
                            var rows = await updateOrder.ExecuteNonQueryAsync();
                            if (rows > 0)
                            {
                                repaired++;
                                processedTableNumbers.Add(tableNumber);
                            }
                        }

                        await tx.CommitAsync();
                    }
                    catch
                    {
                        await tx.RollbackAsync();
                    }
                }

                return (repaired, repaired > 0
                    ? $"Recovered {repaired} open table order session link(s)"
                    : "No table sessions were recovered");
            }
            catch (Exception ex)
            {
                return (0, $"Backfill failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Update session status (Occupied → Ordering → FoodServed → Payment → Cleaning → Closed)
        /// </summary>
        public async Task<(bool success, string message)> UpdateSessionStatusAsync(int sessionId, TableSessionStatus newStatus)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                
                var updateQuery = @"
                    UPDATE TableSessions 
                    SET Status = @status, UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @sessionId AND IsActive = TRUE";
                
                using var command = new MySqlCommand(updateQuery, connection);
                command.Parameters.AddWithValue("@sessionId", sessionId);
                command.Parameters.AddWithValue("@status", newStatus.ToString());
                
                var rowsAffected = await command.ExecuteNonQueryAsync();
                
                if (rowsAffected > 0)
                {
                    return (true, $"Status updated to {GetStatusDisplayName(newStatus)}");
                }
                else
                {
                    return (false, "Session not found or already closed");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error updating status: {ex.Message}");
            }
        }

        /// <summary>
        /// Close table session (customers leave)
        /// </summary>
        public async Task<(bool success, string message)> CloseTableSessionAsync(int sessionId)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                
                var updateQuery = @"
                    UPDATE TableSessions 
                    SET Status = 'Closed', 
                        EndTime = CURRENT_TIMESTAMP,
                        IsActive = FALSE,
                        CurrentOrderId = NULL,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @sessionId AND IsActive = TRUE";
                
                using var command = new MySqlCommand(updateQuery, connection);
                command.Parameters.AddWithValue("@sessionId", sessionId);
                
                var rowsAffected = await command.ExecuteNonQueryAsync();
                
                if (rowsAffected > 0)
                {
                    return (true, "Table session closed successfully");
                }
                else
                {
                    return (false, "Session not found or already closed");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error closing session: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all tables with current session information
        /// </summary>
        public async Task<List<RestaurantTable>> GetTablesWithSessionsAsync(int? floorId = null)
        {
            var tables = new List<RestaurantTable>();

            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                
                var query = @"
                    SELECT 
                        t.Id, t.TableNumber, t.FloorId, t.Capacity, t.Shape, t.Status, 
                        t.TableDesignIcon, COALESCE(t.PositionX, 0) AS PositionX, COALESCE(t.PositionY, 0) AS PositionY,
                        t.CreatedDate, t.UpdatedDate, t.IsActive, t.CurrentSessionId, 
                        t.LastOccupied, t.TotalSessionsToday,
                        f.Name AS FloorName,
                        s.Id AS SessionId, s.SessionNumber, s.PartySize, s.StartTime, 
                        s.CurrentOrderId, s.ParentSessionId, s.MergedIntoSessionId,
                        s.Status AS SessionStatus, s.CustomerNotes, s.SpecialOccasion,
                        s.EstimatedDuration,
                        o.Id AS LinkedOrderDbId,
                        o.order_id AS LinkedOrderId,
                        o.order_number AS LinkedOrderNumber,
                        o.local_lifecycle_state AS LinkedOrderLifecycleState,
                        o.updated_at AS LinkedOrderUpdatedAt,
                        COALESCE(o.is_open, TRUE) AS LinkedOrderIsOpen,
                        COALESCE(o.draft_abandoned_flag, FALSE) AS LinkedOrderDraftAbandonedFlag,
                        TIMESTAMPDIFF(MINUTE, s.StartTime, NOW()) AS MinutesOccupied
                    FROM RestaurantTables t
                    INNER JOIN Floors f ON t.FloorId = f.Id
                    LEFT JOIN TableSessions s ON t.CurrentSessionId = s.Id AND s.IsActive = TRUE
                    LEFT JOIN Orders o ON o.Id = (
                        SELECT o2.Id
                        FROM Orders o2
                        WHERE o2.table_session_id = s.Id
                          AND COALESCE(o2.source_channel, 'local') = 'local'
                          AND COALESCE(o2.is_open, TRUE) = TRUE
                          AND COALESCE(LOWER(o2.local_lifecycle_state), 'active') NOT IN ('paid', 'voided')
                        ORDER BY o2.updated_at DESC, o2.Id DESC
                        LIMIT 1
                    )
                    WHERE t.IsActive = TRUE AND f.IsActive = TRUE";
                
                if (floorId.HasValue)
                {
                    query += " AND t.FloorId = @floorId";
                }
                
                query += " ORDER BY f.Name, t.TableNumber";

                using var command = new MySqlCommand(query, connection);
                if (floorId.HasValue)
                {
                    command.Parameters.AddWithValue("@floorId", floorId.Value);
                }

                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
                while (reader.Read())
                {
                    var table = new RestaurantTable
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        TableNumber = reader.GetString(reader.GetOrdinal("TableNumber")),
                        FloorId = reader.GetInt32(reader.GetOrdinal("FloorId")),
                        Capacity = reader.GetInt32(reader.GetOrdinal("Capacity")),
                        Shape = Enum.Parse<TableShape>(reader.GetString(reader.GetOrdinal("Shape"))),
                        Status = Enum.Parse<TableStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                        TableDesignIcon = reader.IsDBNull(reader.GetOrdinal("TableDesignIcon")) ? "table_1.png" : reader.GetString(reader.GetOrdinal("TableDesignIcon")),
                        PositionX = reader.GetInt32(reader.GetOrdinal("PositionX")),
                        PositionY = reader.GetInt32(reader.GetOrdinal("PositionY")),
                        CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                        UpdatedDate = reader.GetDateTime(reader.GetOrdinal("UpdatedDate")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                        CurrentSessionId = reader.IsDBNull(reader.GetOrdinal("CurrentSessionId")) ? null : reader.GetInt32(reader.GetOrdinal("CurrentSessionId")),
                        LastOccupied = reader.IsDBNull(reader.GetOrdinal("LastOccupied")) ? null : reader.GetDateTime(reader.GetOrdinal("LastOccupied")),
                        TotalSessionsToday = reader.GetInt32(reader.GetOrdinal("TotalSessionsToday")),
                        FloorName = reader.GetString(reader.GetOrdinal("FloorName"))
                    };

                    // Add session information if exists
                    if (!reader.IsDBNull(reader.GetOrdinal("SessionId")))
                    {
                        var linkedOrderLifecycleState = reader.IsDBNull(reader.GetOrdinal("LinkedOrderLifecycleState"))
                            ? null
                            : reader.GetString(reader.GetOrdinal("LinkedOrderLifecycleState"));

                        table.CurrentSession = new TableSession
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("SessionId")),
                            SessionNumber = reader.GetString(reader.GetOrdinal("SessionNumber")),
                            PartySize = reader.GetInt32(reader.GetOrdinal("PartySize")),
                            StartTime = reader.GetDateTime(reader.GetOrdinal("StartTime")),
                            CurrentOrderId = reader.IsDBNull(reader.GetOrdinal("CurrentOrderId")) ? null : Convert.ToString(reader["CurrentOrderId"]),
                            ParentSessionId = reader.IsDBNull(reader.GetOrdinal("ParentSessionId")) ? null : reader.GetInt32(reader.GetOrdinal("ParentSessionId")),
                            MergedIntoSessionId = reader.IsDBNull(reader.GetOrdinal("MergedIntoSessionId")) ? null : reader.GetInt32(reader.GetOrdinal("MergedIntoSessionId")),
                            Status = Enum.Parse<TableSessionStatus>(reader.GetString(reader.GetOrdinal("SessionStatus"))),
                            CustomerNotes = reader.IsDBNull(reader.GetOrdinal("CustomerNotes")) ? null : reader.GetString(reader.GetOrdinal("CustomerNotes")),
                            SpecialOccasion = reader.IsDBNull(reader.GetOrdinal("SpecialOccasion")) ? null : reader.GetString(reader.GetOrdinal("SpecialOccasion")),
                            EstimatedDuration = reader.GetInt32(reader.GetOrdinal("EstimatedDuration")),
                            LinkedOrderDbId = reader.IsDBNull(reader.GetOrdinal("LinkedOrderDbId")) ? null : reader.GetInt32(reader.GetOrdinal("LinkedOrderDbId")),
                            LinkedOrderId = reader.IsDBNull(reader.GetOrdinal("LinkedOrderId")) ? null : reader.GetString(reader.GetOrdinal("LinkedOrderId")),
                            LinkedOrderNumber = reader.IsDBNull(reader.GetOrdinal("LinkedOrderNumber")) ? null : reader.GetString(reader.GetOrdinal("LinkedOrderNumber")),
                            LinkedOrderLifecycleState = linkedOrderLifecycleState,
                            LinkedOrderUpdatedAt = reader.IsDBNull(reader.GetOrdinal("LinkedOrderUpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LinkedOrderUpdatedAt")),
                            LinkedOrderIsOpen = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderIsOpen")) && reader.GetBoolean(reader.GetOrdinal("LinkedOrderIsOpen")),
                            LinkedOrderDraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderDraftAbandonedFlag")) && reader.GetBoolean(reader.GetOrdinal("LinkedOrderDraftAbandonedFlag"))
                        };
                    }

                    tables.Add(table);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading tables with sessions: {ex.Message}");
            }

            return tables;
        }

        /// <summary>
        /// Add a note to a table session
        /// </summary>
        public async Task<(bool success, string message)> AddSessionNoteAsync(int sessionId, string note, SessionNoteType noteType, string createdBy)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                var insertQuery = @"
                    INSERT INTO SessionNotes (SessionId, Note, NoteType, CreatedBy)
                    VALUES (@sessionId, @note, @noteType, @createdBy)";
                
                using var command = new MySqlCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@sessionId", sessionId);
                command.Parameters.AddWithValue("@note", note);
                command.Parameters.AddWithValue("@noteType", noteType.ToString());
                command.Parameters.AddWithValue("@createdBy", createdBy);
                
                await command.ExecuteNonQueryAsync();
                
                return (true, "Note added successfully");
            }
            catch (Exception ex)
            {
                return (false, $"Error adding note: {ex.Message}");
            }
        }

        /// <summary>
        /// Get session notes for a table session
        /// </summary>
        public async Task<List<SessionNote>> GetSessionNotesAsync(int sessionId)
        {
            var notes = new List<SessionNote>();

            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                
                var query = @"
                    SELECT Id, SessionId, Note, NoteType, CreatedBy, CreatedDate
                    FROM SessionNotes
                    WHERE SessionId = @sessionId
                    ORDER BY CreatedDate DESC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@sessionId", sessionId);

                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
                while (reader.Read())
                {
                    notes.Add(new SessionNote
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        SessionId = reader.GetInt32(reader.GetOrdinal("SessionId")),
                        Note = reader.GetString(reader.GetOrdinal("Note")),
                        NoteType = Enum.Parse<SessionNoteType>(reader.GetString(reader.GetOrdinal("NoteType"))),
                        CreatedBy = reader.IsDBNull(reader.GetOrdinal("CreatedBy")) ? null : reader.GetString(reader.GetOrdinal("CreatedBy")),
                        CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate"))
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading session notes: {ex.Message}");
            }

            return notes;
        }

        /// <summary>
        /// Generate next session number (S001, S002, etc.)
        /// </summary>
        private async Task<string> GenerateSessionNumberAsync()
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                return await GenerateSessionNumberAsync(connection, null);
            }
            catch
            {
                return $"S{DateTime.Now:HHmmss}"; // Fallback to timestamp
            }
        }

        private static async Task<string> GenerateSessionNumberAsync(MySqlConnection connection, MySqlTransaction? transaction)
        {
            var query = @"
                SELECT COUNT(*) + 1 AS NextNumber 
                FROM TableSessions 
                WHERE DATE(CreatedDate) = CURDATE()";

            using var command = transaction == null
                ? new MySqlCommand(query, connection)
                : new MySqlCommand(query, connection, transaction);
            var nextNumber = Convert.ToInt32(await command.ExecuteScalarAsync());

            return $"S{nextNumber:D3}";
        }

        /// <summary>
        /// Get display name for session status
        /// </summary>
        private string GetStatusDisplayName(TableSessionStatus status) => status switch
        {
            TableSessionStatus.Occupied => "Just Seated",
            TableSessionStatus.Ordering => "Taking Order", 
            TableSessionStatus.FoodServed => "Dining",
            TableSessionStatus.Payment => "Ready to Pay",
            TableSessionStatus.Cleaning => "Cleaning",
            TableSessionStatus.Closed => "Closed",
            _ => status.ToString()
        };

        public async Task<(bool success, string message)> LinkOrderToSessionAsync(int sessionId, string orderId, TableSessionStatus? nextStatus = null, string? actorName = null)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();

                var session = await GetSessionByIdAsync(connection, transaction, sessionId);
                if (session == null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Session not found");
                }

                var orderReference = await ResolveSessionOrderReferenceAsync(connection, transaction, orderId);

                var statusToStore = nextStatus ?? (session.Status == TableSessionStatus.Occupied ? TableSessionStatus.Ordering : session.Status);
                var currentOrderRef = string.IsNullOrWhiteSpace(session.CurrentOrderId) ? null : session.CurrentOrderId.Trim();
                var nextOrderRef = orderReference?.ToString()?.Trim();
                var isSameOrderReference = !string.IsNullOrWhiteSpace(currentOrderRef)
                    && !string.IsNullOrWhiteSpace(nextOrderRef)
                    && string.Equals(currentOrderRef, nextOrderRef, StringComparison.OrdinalIgnoreCase);
                var isSameStatus = session.Status == statusToStore;

                if (isSameOrderReference && isSameStatus)
                {
                    using var ensureOrderLink = new MySqlCommand(@"
                        UPDATE orders
                        SET table_session_id = @sessionId
                        WHERE order_id = @orderId
                          AND (table_session_id IS NULL OR table_session_id <> @sessionId)", connection, transaction);
                    ensureOrderLink.Parameters.AddWithValue("@sessionId", sessionId);
                    ensureOrderLink.Parameters.AddWithValue("@orderId", orderId);
                    await ensureOrderLink.ExecuteNonQueryAsync();

                    await transaction.CommitAsync();
                    return (true, "Session already linked");
                }

                var updateQuery = orderReference != null
                    ? @"
                    UPDATE TableSessions
                    SET CurrentOrderId = @orderReference,
                        Status = @status,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @sessionId AND IsActive = TRUE"
                    : @"
                    UPDATE TableSessions
                    SET Status = @status,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @sessionId AND IsActive = TRUE";

                using (var command = new MySqlCommand(updateQuery, connection, transaction))
                {
                    if (orderReference != null)
                    {
                        command.Parameters.AddWithValue("@orderReference", orderReference);
                    }
                    command.Parameters.AddWithValue("@status", statusToStore.ToString());
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        await transaction.RollbackAsync();
                        return (false, "Session not found or inactive");
                    }
                }

                using (var updateOrder = new MySqlCommand(@"
                    UPDATE orders
                    SET table_session_id = @sessionId
                    WHERE order_id = @orderId
                      AND (table_session_id IS NULL OR table_session_id <> @sessionId)", connection, transaction))
                {
                    updateOrder.Parameters.AddWithValue("@sessionId", sessionId);
                    updateOrder.Parameters.AddWithValue("@orderId", orderId);
                    await updateOrder.ExecuteNonQueryAsync();
                }

                using (var closeOlderOpenOrders = new MySqlCommand(@"
                    UPDATE orders
                    SET is_open = 0,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE table_session_id = @sessionId
                      AND order_id <> @orderId
                      AND COALESCE(source_channel, 'local') = 'local'
                      AND COALESCE(order_type, 'table') = 'table'
                      AND COALESCE(is_open, 1) = 1", connection, transaction))
                {
                    closeOlderOpenOrders.Parameters.AddWithValue("@sessionId", sessionId);
                    closeOlderOpenOrders.Parameters.AddWithValue("@orderId", orderId);
                    await closeOlderOpenOrders.ExecuteNonQueryAsync();
                }

                await InsertSessionEventAsync(connection, transaction, sessionId, "state_changed", actorName, JsonSerializer.Serialize(new
                {
                    orderId,
                    fromStatus = session.Status.ToString(),
                    toStatus = statusToStore.ToString()
                }));

                await transaction.CommitAsync();
                return (true, $"Session updated to {GetStatusDisplayName(statusToStore)}");
            }
            catch (Exception ex)
            {
                return (false, $"Error linking order to session: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> MarkSessionFoodServedAsync(int sessionId, string? actorName = null)
        {
            return await TransitionSessionAsync(sessionId, TableSessionStatus.FoodServed, actorName);
        }

        public async Task<(bool success, string message)> MarkTableOccupiedAsync(int sessionId)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();

                var session = await GetSessionByIdAsync(connection, transaction, sessionId);
                if (session == null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Session not found");
                }

                using (var command = new MySqlCommand(@"
                    UPDATE RestaurantTables
                    SET Status = 'Occupied',
                        CurrentSessionId = @sessionId,
                        LastOccupied = CURRENT_TIMESTAMP,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @tableId", connection, transaction))
                {
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    command.Parameters.AddWithValue("@tableId", session.TableId);
                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        await transaction.RollbackAsync();
                        return (false, "Table not found for session");
                    }
                }

                await transaction.CommitAsync();
                return (true, "Table marked occupied");
            }
            catch (Exception ex)
            {
                return (false, $"Error marking table occupied: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> MarkSessionPaymentAsync(int sessionId, string? actorName = null)
        {
            return await TransitionSessionAsync(sessionId, TableSessionStatus.Payment, actorName);
        }

        public async Task<(bool success, string message)> CloseSessionForOrderAsync(int sessionId, string outcome, string? actorName = null, string? payloadJson = null)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();
                var session = await GetSessionByIdAsync(connection, transaction, sessionId);
                if (session == null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Session not found");
                }

                var updateQuery = @"
                    UPDATE TableSessions
                    SET Status = 'Closed',
                        EndTime = CURRENT_TIMESTAMP,
                        ActualDuration = TIMESTAMPDIFF(MINUTE, StartTime, CURRENT_TIMESTAMP),
                        CurrentOrderId = NULL,
                        IsActive = FALSE,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @sessionId AND IsActive = TRUE";

                using (var command = new MySqlCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        await transaction.RollbackAsync();
                        return (false, "Session not found or already closed");
                    }
                }

                using (var updateTable = new MySqlCommand(@"
                    UPDATE RestaurantTables
                    SET Status = 'Available',
                        CurrentSessionId = NULL,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @tableId", connection, transaction))
                {
                    updateTable.Parameters.AddWithValue("@tableId", session.TableId);
                    await updateTable.ExecuteNonQueryAsync();
                }

                await InsertSessionEventAsync(connection, transaction, sessionId, outcome, actorName, payloadJson ?? JsonSerializer.Serialize(new
                {
                    outcome,
                    currentOrderId = session.CurrentOrderId
                }));

                await transaction.CommitAsync();
                return (true, "Session closed successfully");
            }
            catch (Exception ex)
            {
                return (false, $"Error closing session: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> ForceReleaseTableAsync(int tableId, string outcome = "force_released", string? actorName = null, string? payloadJson = null)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();

                var activeSessionIds = new List<int>();
                using (var findSessions = new MySqlCommand(@"
                    SELECT Id
                    FROM TableSessions
                    WHERE TableId = @tableId
                      AND IsActive = TRUE", connection, transaction))
                {
                    findSessions.Parameters.AddWithValue("@tableId", tableId);
                    using var reader = (MySqlDataReader)await findSessions.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        activeSessionIds.Add(reader.GetInt32(reader.GetOrdinal("Id")));
                    }
                }

                using (var closeSessions = new MySqlCommand(@"
                    UPDATE TableSessions
                    SET Status = 'Closed',
                        EndTime = COALESCE(EndTime, CURRENT_TIMESTAMP),
                        ActualDuration = COALESCE(ActualDuration, TIMESTAMPDIFF(MINUTE, StartTime, CURRENT_TIMESTAMP)),
                        CurrentOrderId = NULL,
                        IsActive = FALSE,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE TableId = @tableId
                      AND IsActive = TRUE", connection, transaction))
                {
                    closeSessions.Parameters.AddWithValue("@tableId", tableId);
                    await closeSessions.ExecuteNonQueryAsync();
                }

                using (var clearTable = new MySqlCommand(@"
                    UPDATE RestaurantTables
                    SET Status = 'Available',
                        CurrentSessionId = NULL,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @tableId", connection, transaction))
                {
                    clearTable.Parameters.AddWithValue("@tableId", tableId);
                    var rows = await clearTable.ExecuteNonQueryAsync();
                    if (rows == 0)
                    {
                        await transaction.RollbackAsync();
                        return (false, "Table not found");
                    }
                }

                foreach (var sessionId in activeSessionIds)
                {
                    await InsertSessionEventAsync(connection, transaction, sessionId, outcome, actorName, payloadJson ?? JsonSerializer.Serialize(new
                    {
                        outcome,
                        releaseMode = "force",
                        tableId
                    }));
                }

                await transaction.CommitAsync();
                return (true, $"Table released ({activeSessionIds.Count} active session(s) closed)");
            }
            catch (Exception ex)
            {
                return (false, $"Error force-releasing table: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> ResetAllTableStateToEmptyAsync()
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();

                using (var closeSessions = new MySqlCommand(@"
                    UPDATE TableSessions
                    SET Status = 'Closed',
                        EndTime = COALESCE(EndTime, CURRENT_TIMESTAMP),
                        CurrentOrderId = NULL,
                        IsActive = FALSE,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE IsActive = TRUE", connection, transaction))
                {
                    await closeSessions.ExecuteNonQueryAsync();
                }

                using (var clearTables = new MySqlCommand(@"
                    UPDATE RestaurantTables
                    SET Status = 'Available',
                        CurrentSessionId = NULL,
                        LastOccupied = NULL,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE IsActive = TRUE", connection, transaction))
                {
                    await clearTables.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                return (true, "All tables reset to empty");
            }
            catch (Exception ex)
            {
                return (false, $"Error resetting tables: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> TransferSessionAsync(int sessionId, int targetTableId, string? actorName = null, string? reason = null)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();

                var session = await GetSessionByIdAsync(connection, transaction, sessionId);
                if (session == null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Session not found");
                }

                var sourceTableId = session!.TableId;

                var targetTable = await GetTableByIdAsync(connection, transaction, targetTableId);
                if (targetTable == null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Target table not found");
                }

                if (!string.Equals(targetTable["Status"].ToString(), "Available", StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return (false, "Target table is not available");
                }

                using (var updateSession = new MySqlCommand(@"
                    UPDATE TableSessions
                    SET TableId = @targetTableId,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @sessionId AND IsActive = TRUE", connection, transaction))
                {
                    updateSession.Parameters.AddWithValue("@targetTableId", targetTableId);
                    updateSession.Parameters.AddWithValue("@sessionId", sessionId);
                    var rows = await updateSession.ExecuteNonQueryAsync();
                    if (rows == 0)
                    {
                        await transaction.RollbackAsync();
                        return (false, "Session could not be transferred");
                    }
                }

                using (var clearOldTable = new MySqlCommand(@"
                    UPDATE RestaurantTables
                    SET CurrentSessionId = NULL,
                        Status = 'Available',
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @tableId", connection, transaction))
                {
                    clearOldTable.Parameters.AddWithValue("@tableId", sourceTableId);
                    await clearOldTable.ExecuteNonQueryAsync();
                }

                using (var assignNewTable = new MySqlCommand(@"
                    UPDATE RestaurantTables
                    SET CurrentSessionId = @sessionId,
                        Status = 'Occupied',
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @tableId", connection, transaction))
                {
                    assignNewTable.Parameters.AddWithValue("@sessionId", sessionId);
                    assignNewTable.Parameters.AddWithValue("@tableId", targetTableId);
                    await assignNewTable.ExecuteNonQueryAsync();
                }

                await InsertSessionEventAsync(connection, transaction, sessionId, "table_transferred", actorName, JsonSerializer.Serialize(new
                {
                    reason,
                    fromTableId = session.TableId,
                    toTableId = targetTableId,
                    currentOrderId = session.CurrentOrderId
                }));

                await transaction.CommitAsync();
                return (true, "Session transferred successfully");
            }
            catch (Exception ex)
            {
                return (false, $"Error transferring session: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> MergeSessionsAsync(int parentSessionId, int childSessionId, string? actorName = null, string? reason = null)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();

                var parentSession = await GetSessionByIdAsync(connection, transaction, parentSessionId);
                var childSession = await GetSessionByIdAsync(connection, transaction, childSessionId);

                if (parentSession == null || childSession == null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Parent or child session not found");
                }

                if (!parentSession.IsActive || !childSession.IsActive)
                {
                    await transaction.RollbackAsync();
                    return (false, "One or both sessions are inactive");
                }

                var mergedOrderId = parentSession.CurrentOrderId ?? childSession.CurrentOrderId;

                using (var updateChildSession = new MySqlCommand(@"
                    UPDATE TableSessions
                    SET ParentSessionId = @parentSessionId,
                        MergedIntoSessionId = @parentSessionId,
                        CurrentOrderId = NULL,
                        Status = 'Closed',
                        EndTime = CURRENT_TIMESTAMP,
                        IsActive = FALSE,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @childSessionId", connection, transaction))
                {
                    updateChildSession.Parameters.AddWithValue("@parentSessionId", parentSessionId);
                    updateChildSession.Parameters.AddWithValue("@childSessionId", childSessionId);
                    await updateChildSession.ExecuteNonQueryAsync();
                }

                using (var updateParentSession = new MySqlCommand(@"
                    UPDATE TableSessions
                    SET CurrentOrderId = @orderId,
                        PartySize = PartySize + @childPartySize,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @parentSessionId AND IsActive = TRUE", connection, transaction))
                {
                    updateParentSession.Parameters.AddWithValue("@orderId", mergedOrderId ?? (object)DBNull.Value);
                    updateParentSession.Parameters.AddWithValue("@childPartySize", childSession.PartySize);
                    updateParentSession.Parameters.AddWithValue("@parentSessionId", parentSessionId);
                    await updateParentSession.ExecuteNonQueryAsync();
                }

                using (var updateChildTable = new MySqlCommand(@"
                    UPDATE RestaurantTables
                    SET CurrentSessionId = NULL,
                        Status = 'Available',
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @tableId", connection, transaction))
                {
                    updateChildTable.Parameters.AddWithValue("@tableId", childSession.TableId);
                    await updateChildTable.ExecuteNonQueryAsync();
                }

                await InsertSessionEventAsync(connection, transaction, parentSessionId, "table_merged", actorName, JsonSerializer.Serialize(new
                {
                    reason,
                    childSessionId,
                    parentSessionId,
                    mergedOrderId
                }));

                await InsertSessionEventAsync(connection, transaction, childSessionId, "table_merged", actorName, JsonSerializer.Serialize(new
                {
                    reason,
                    childSessionId,
                    parentSessionId,
                    mergedOrderId,
                    relation = "child"
                }));

                await transaction.CommitAsync();
                return (true, "Sessions merged successfully");
            }
            catch (Exception ex)
            {
                return (false, $"Error merging sessions: {ex.Message}");
            }
        }

        public async Task<TableSession?> GetSessionByIdAsync(int sessionId)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                return await GetSessionByIdAsync(connection, null!, sessionId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading session by id: {ex.Message}");
                return null;
            }
        }

        public async Task<TableSession?> GetActiveSessionByTableIdAsync(int tableId)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                return await GetActiveSessionByTableIdAsync(connection, null, tableId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading active session by table id: {ex.Message}");
                return null;
            }
        }

        private static string? ParseTableNumberFromCustomerName(string customerName)
        {
            if (string.IsNullOrWhiteSpace(customerName))
            {
                return null;
            }

            var trimmed = customerName.Trim();
            if (!trimmed.StartsWith("Table ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tableNumber = trimmed[6..].Trim();
            return string.IsNullOrWhiteSpace(tableNumber) ? null : tableNumber;
        }

        private async Task<TableSession?> GetActiveSessionByTableIdAsync(MySqlConnection connection, MySqlTransaction? transaction, int tableId)
        {
            const string query = @"
                SELECT Id, TableId, SessionNumber, PartySize, StartTime, EndTime, Status,
                       CustomerNotes, SpecialOccasion, EstimatedDuration, ActualDuration,
                       CreatedDate, UpdatedDate, IsActive, CurrentOrderId, ParentSessionId, MergedIntoSessionId
                FROM TableSessions
                WHERE TableId = @tableId AND IsActive = TRUE
                ORDER BY StartTime DESC, Id DESC
                LIMIT 1";

            using var command = transaction == null
                ? new MySqlCommand(query, connection)
                : new MySqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@tableId", tableId);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new TableSession
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                TableId = reader.GetInt32(reader.GetOrdinal("TableId")),
                SessionNumber = reader.GetString(reader.GetOrdinal("SessionNumber")),
                PartySize = reader.GetInt32(reader.GetOrdinal("PartySize")),
                StartTime = reader.GetDateTime(reader.GetOrdinal("StartTime")),
                EndTime = reader.IsDBNull(reader.GetOrdinal("EndTime")) ? null : reader.GetDateTime(reader.GetOrdinal("EndTime")),
                Status = Enum.Parse<TableSessionStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                CustomerNotes = reader.IsDBNull(reader.GetOrdinal("CustomerNotes")) ? null : reader.GetString(reader.GetOrdinal("CustomerNotes")),
                SpecialOccasion = reader.IsDBNull(reader.GetOrdinal("SpecialOccasion")) ? null : reader.GetString(reader.GetOrdinal("SpecialOccasion")),
                EstimatedDuration = reader.GetInt32(reader.GetOrdinal("EstimatedDuration")),
                ActualDuration = reader.IsDBNull(reader.GetOrdinal("ActualDuration")) ? null : reader.GetInt32(reader.GetOrdinal("ActualDuration")),
                CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                UpdatedDate = reader.GetDateTime(reader.GetOrdinal("UpdatedDate")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                CurrentOrderId = reader.IsDBNull(reader.GetOrdinal("CurrentOrderId")) ? null : Convert.ToString(reader["CurrentOrderId"]),
                ParentSessionId = reader.IsDBNull(reader.GetOrdinal("ParentSessionId")) ? null : reader.GetInt32(reader.GetOrdinal("ParentSessionId")),
                MergedIntoSessionId = reader.IsDBNull(reader.GetOrdinal("MergedIntoSessionId")) ? null : reader.GetInt32(reader.GetOrdinal("MergedIntoSessionId"))
            };
        }

        public async Task<(bool success, string message)> TransitionSessionAsync(int sessionId, TableSessionStatus newStatus, string? actorName = null)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureSessionOrchestrationSchemaAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();

                var session = await GetSessionByIdAsync(connection, transaction, sessionId);
                if (session == null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Session not found");
                }

                using (var command = new MySqlCommand(@"
                    UPDATE TableSessions
                    SET Status = @status,
                        UpdatedDate = CURRENT_TIMESTAMP
                    WHERE Id = @sessionId AND IsActive = TRUE", connection, transaction))
                {
                    command.Parameters.AddWithValue("@status", newStatus.ToString());
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        await transaction.RollbackAsync();
                        return (false, "Session not found or inactive");
                    }
                }

                await InsertSessionEventAsync(connection, transaction, sessionId, "state_changed", actorName, JsonSerializer.Serialize(new
                {
                    fromStatus = session.Status.ToString(),
                    toStatus = newStatus.ToString(),
                    currentOrderId = session.CurrentOrderId
                }));

                await transaction.CommitAsync();
                return (true, $"Status updated to {GetStatusDisplayName(newStatus)}");
            }
            catch (Exception ex)
            {
                return (false, $"Error updating session status: {ex.Message}");
            }
        }

        private async Task EnsureSessionOrchestrationSchemaAsync(MySqlConnection connection)
        {
            try
            {
                var createEventsTable = @"
                    CREATE TABLE IF NOT EXISTS TableSessionEvents (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        SessionId INT NOT NULL,
                        EventType VARCHAR(50) NOT NULL,
                        ActorName VARCHAR(100) NULL,
                        PayloadJson JSON NULL,
                        CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (SessionId) REFERENCES TableSessions(Id) ON DELETE CASCADE,
                        INDEX idx_session_events_session_created (SessionId, CreatedDate),
                        INDEX idx_session_events_type_created (EventType, CreatedDate)
                    ) ENGINE=InnoDB";

                using var createEventsCommand = new MySqlCommand(createEventsTable, connection);
                await createEventsCommand.ExecuteNonQueryAsync();

                var alterSessionsSql = @"
                    ALTER TABLE TableSessions
                    ADD COLUMN IF NOT EXISTS CurrentOrderId VARCHAR(100) NULL,
                    ADD COLUMN IF NOT EXISTS ParentSessionId INT NULL,
                    ADD COLUMN IF NOT EXISTS MergedIntoSessionId INT NULL";

                using var alterSessionsCommand = new MySqlCommand(alterSessionsSql, connection);
                await alterSessionsCommand.ExecuteNonQueryAsync();

                try
                {
                    using var addOrderFkCommand = new MySqlCommand(@"
                        ALTER TABLE TableSessions
                        ADD CONSTRAINT fk_table_sessions_current_order
                        FOREIGN KEY (CurrentOrderId) REFERENCES orders(order_id) ON DELETE SET NULL", connection);
                    await addOrderFkCommand.ExecuteNonQueryAsync();
                }
                catch
                {
                }

                try
                {
                    using var addParentFkCommand = new MySqlCommand(@"
                        ALTER TABLE TableSessions
                        ADD CONSTRAINT fk_table_sessions_parent_session
                        FOREIGN KEY (ParentSessionId) REFERENCES TableSessions(Id) ON DELETE SET NULL", connection);
                    await addParentFkCommand.ExecuteNonQueryAsync();
                }
                catch
                {
                }

                try
                {
                    using var addMergedFkCommand = new MySqlCommand(@"
                        ALTER TABLE TableSessions
                        ADD CONSTRAINT fk_table_sessions_merged_into_session
                        FOREIGN KEY (MergedIntoSessionId) REFERENCES TableSessions(Id) ON DELETE SET NULL", connection);
                    await addMergedFkCommand.ExecuteNonQueryAsync();
                }
                catch
                {
                }

                if (!_sessionSchemaMaintenanceChecked)
                {
                    await RemoveLegacyUniqueActiveSessionConstraintAsync(connection);
                    await NormalizeTerminalSessionsAndTableStateAsync(connection);
                    _sessionSchemaMaintenanceChecked = true;
                }

                await EnsureTableSessionStatusTriggerAsync(connection);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Session orchestration schema warning: {ex.Message}");
            }
        }

        private async Task RemoveLegacyUniqueActiveSessionConstraintAsync(MySqlConnection connection)
        {
            try
            {
                var uniqueIndexes = new List<string>();
                const string query = @"
                    SELECT INDEX_NAME
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TableSessions'
                      AND NON_UNIQUE = 0
                    GROUP BY INDEX_NAME
                    HAVING COUNT(*) = 2
                       AND SUM(CASE WHEN COLUMN_NAME = 'TableId' THEN 1 ELSE 0 END) = 1
                       AND SUM(CASE WHEN COLUMN_NAME = 'IsActive' THEN 1 ELSE 0 END) = 1";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = (MySqlDataReader)await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var indexName = reader["INDEX_NAME"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(indexName))
                        {
                            uniqueIndexes.Add(indexName);
                        }
                    }
                }

                foreach (var indexName in uniqueIndexes)
                {
                    var safeIndexName = indexName.Replace("`", "``");
                    using var dropCommand = new MySqlCommand($"DROP INDEX `{safeIndexName}` ON TableSessions", connection);
                    await dropCommand.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine($"[TableSession] Removed legacy unique index: {indexName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TableSession] Unique session index normalization warning: {ex.Message}");
            }
        }

        private async Task NormalizeTerminalSessionsAndTableStateAsync(MySqlConnection connection)
        {
            try
            {
                using (var closeTerminalSessions = new MySqlCommand(@"
                    UPDATE TableSessions s
                    INNER JOIN orders o ON o.table_session_id = s.Id
                    SET s.Status = 'Closed',
                        s.EndTime = COALESCE(s.EndTime, o.paid_at, o.voided_at, CURRENT_TIMESTAMP),
                        s.ActualDuration = COALESCE(
                            s.ActualDuration,
                            TIMESTAMPDIFF(MINUTE, s.StartTime, COALESCE(o.paid_at, o.voided_at, CURRENT_TIMESTAMP))
                        ),
                        s.CurrentOrderId = NULL,
                        s.IsActive = FALSE,
                        s.UpdatedDate = CURRENT_TIMESTAMP
                    WHERE s.IsActive = TRUE
                      AND COALESCE(o.source_channel, 'local') = 'local'
                      AND COALESCE(o.order_type, 'table') = 'table'
                      AND (
                          COALESCE(o.is_open, TRUE) = FALSE
                          OR COALESCE(LOWER(o.local_lifecycle_state), 'active') IN ('paid', 'voided')
                      )", connection))
                {
                    await closeTerminalSessions.ExecuteNonQueryAsync();
                }

                using (var syncAvailableTables = new MySqlCommand(@"
                    UPDATE RestaurantTables t
                    LEFT JOIN TableSessions s ON t.CurrentSessionId = s.Id AND s.IsActive = TRUE
                    SET t.Status = 'Available',
                        t.CurrentSessionId = NULL,
                        t.UpdatedDate = CURRENT_TIMESTAMP
                    WHERE t.IsActive = TRUE
                      AND s.Id IS NULL", connection))
                {
                    await syncAvailableTables.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TableSession] Terminal session normalization warning: {ex.Message}");
            }
        }

        private async Task EnsureTableSessionStatusTriggerAsync(MySqlConnection connection)
        {
            try
            {
                using (var dropTrigger = new MySqlCommand("DROP TRIGGER IF EXISTS update_table_status_on_session", connection))
                {
                    await dropTrigger.ExecuteNonQueryAsync();
                }

                const string createTriggerSql = @"
                    CREATE TRIGGER update_table_status_on_session
                    AFTER UPDATE ON TableSessions
                    FOR EACH ROW
                    BEGIN
                        IF NEW.Status = 'Closed' AND OLD.Status <> 'Closed' THEN
                            UPDATE RestaurantTables
                            SET Status = 'Available',
                                CurrentSessionId = NULL,
                                LastOccupied = COALESCE(NEW.EndTime, CURRENT_TIMESTAMP)
                            WHERE Id = NEW.TableId;
                        ELSEIF NEW.Status IN ('Occupied', 'Ordering', 'FoodServed', 'Payment') THEN
                            UPDATE RestaurantTables
                            SET Status = 'Occupied',
                                CurrentSessionId = NEW.Id
                            WHERE Id = NEW.TableId;
                        ELSEIF NEW.Status = 'Cleaning' THEN
                            UPDATE RestaurantTables
                            SET Status = 'Reserved'
                            WHERE Id = NEW.TableId;
                        END IF;
                    END";

                using var createTrigger = new MySqlCommand(createTriggerSql, connection);
                await createTrigger.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Session trigger sync warning: {ex.Message}");
            }
        }

        private async Task<TableSession?> GetSessionByIdAsync(MySqlConnection connection, MySqlTransaction transaction, int sessionId)
        {
            const string query = @"
                SELECT Id, TableId, SessionNumber, PartySize, StartTime, EndTime, Status,
                       CustomerNotes, SpecialOccasion, EstimatedDuration, ActualDuration,
                       CreatedDate, UpdatedDate, IsActive, CurrentOrderId, ParentSessionId, MergedIntoSessionId
                FROM TableSessions
                WHERE Id = @sessionId
                LIMIT 1";

            using var command = transaction == null
                ? new MySqlCommand(query, connection)
                : new MySqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@sessionId", sessionId);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new TableSession
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                TableId = reader.GetInt32(reader.GetOrdinal("TableId")),
                SessionNumber = reader.GetString(reader.GetOrdinal("SessionNumber")),
                PartySize = reader.GetInt32(reader.GetOrdinal("PartySize")),
                StartTime = reader.GetDateTime(reader.GetOrdinal("StartTime")),
                EndTime = reader.IsDBNull(reader.GetOrdinal("EndTime")) ? null : reader.GetDateTime(reader.GetOrdinal("EndTime")),
                Status = Enum.Parse<TableSessionStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                CustomerNotes = reader.IsDBNull(reader.GetOrdinal("CustomerNotes")) ? null : reader.GetString(reader.GetOrdinal("CustomerNotes")),
                SpecialOccasion = reader.IsDBNull(reader.GetOrdinal("SpecialOccasion")) ? null : reader.GetString(reader.GetOrdinal("SpecialOccasion")),
                EstimatedDuration = reader.GetInt32(reader.GetOrdinal("EstimatedDuration")),
                ActualDuration = reader.IsDBNull(reader.GetOrdinal("ActualDuration")) ? null : reader.GetInt32(reader.GetOrdinal("ActualDuration")),
                CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                UpdatedDate = reader.GetDateTime(reader.GetOrdinal("UpdatedDate")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                CurrentOrderId = reader.IsDBNull(reader.GetOrdinal("CurrentOrderId")) ? null : Convert.ToString(reader["CurrentOrderId"]),
                ParentSessionId = reader.IsDBNull(reader.GetOrdinal("ParentSessionId")) ? null : reader.GetInt32(reader.GetOrdinal("ParentSessionId")),
                MergedIntoSessionId = reader.IsDBNull(reader.GetOrdinal("MergedIntoSessionId")) ? null : reader.GetInt32(reader.GetOrdinal("MergedIntoSessionId"))
            };
        }

        private async Task<object?> ResolveSessionOrderReferenceAsync(MySqlConnection connection, MySqlTransaction? transaction, string orderId)
        {
            var canStore = await CanStoreCurrentOrderReferenceAsync(connection, transaction);
            if (!canStore)
            {
                return null;
            }

            var usesNumeric = await IsCurrentOrderIdNumericAsync(connection, transaction);
            if (!usesNumeric)
            {
                return orderId;
            }

            using var command = transaction == null
                ? new MySqlCommand("SELECT Id FROM orders WHERE order_id = @orderId LIMIT 1", connection)
                : new MySqlCommand("SELECT Id FROM orders WHERE order_id = @orderId LIMIT 1", connection, transaction);
            command.Parameters.AddWithValue("@orderId", orderId);
            var scalar = await command.ExecuteScalarAsync();
            if (scalar == null || scalar == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt32(scalar);
        }

        private async Task<bool> CanStoreCurrentOrderReferenceAsync(MySqlConnection connection, MySqlTransaction? transaction)
        {
            if (_canStoreCurrentOrderReference.HasValue)
            {
                return _canStoreCurrentOrderReference.Value;
            }

            using var command = transaction == null
                ? new MySqlCommand(@"
                    SELECT REFERENCED_TABLE_NAME
                    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TableSessions'
                      AND COLUMN_NAME = 'CurrentOrderId'
                      AND REFERENCED_TABLE_NAME IS NOT NULL
                    LIMIT 1", connection)
                : new MySqlCommand(@"
                    SELECT REFERENCED_TABLE_NAME
                    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TableSessions'
                      AND COLUMN_NAME = 'CurrentOrderId'
                      AND REFERENCED_TABLE_NAME IS NOT NULL
                    LIMIT 1", connection, transaction);

            var referencedTable = (await command.ExecuteScalarAsync())?.ToString();
            _canStoreCurrentOrderReference = string.IsNullOrWhiteSpace(referencedTable)
                || referencedTable.Equals("orders", StringComparison.OrdinalIgnoreCase);
            return _canStoreCurrentOrderReference.Value;
        }

        private async Task<bool> IsCurrentOrderIdNumericAsync(MySqlConnection connection, MySqlTransaction? transaction)
        {
            if (_isCurrentOrderIdNumeric.HasValue)
            {
                return _isCurrentOrderIdNumeric.Value;
            }

            using var command = transaction == null
                ? new MySqlCommand(@"
                    SELECT DATA_TYPE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TableSessions'
                      AND COLUMN_NAME = 'CurrentOrderId'
                    LIMIT 1", connection)
                : new MySqlCommand(@"
                    SELECT DATA_TYPE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TableSessions'
                      AND COLUMN_NAME = 'CurrentOrderId'
                    LIMIT 1", connection, transaction);
            var dataType = (await command.ExecuteScalarAsync())?.ToString()?.Trim().ToLowerInvariant();
            _isCurrentOrderIdNumeric = dataType is "tinyint" or "smallint" or "mediumint" or "int" or "bigint";
            return _isCurrentOrderIdNumeric.Value;
        }

        private async Task<Dictionary<string, object?>> GetTableByIdAsync(MySqlConnection connection, MySqlTransaction transaction, int tableId)
        {
            const string query = @"
                SELECT Id, TableNumber, Status, CurrentSessionId
                FROM RestaurantTables
                WHERE Id = @tableId
                LIMIT 1";

            using var command = new MySqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@tableId", tableId);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null!;
            }

            return new Dictionary<string, object?>
            {
                ["Id"] = reader.GetInt32(reader.GetOrdinal("Id")),
                ["TableNumber"] = reader.GetString(reader.GetOrdinal("TableNumber")),
                ["Status"] = reader.GetString(reader.GetOrdinal("Status")),
                ["CurrentSessionId"] = reader.IsDBNull(reader.GetOrdinal("CurrentSessionId")) ? null : reader.GetInt32(reader.GetOrdinal("CurrentSessionId"))
            };
        }

        private async Task InsertSessionEventAsync(MySqlConnection connection, MySqlTransaction transaction, int sessionId, string eventType, string? actorName, string? payloadJson)
        {
            const string insertSql = @"
                INSERT INTO TableSessionEvents (SessionId, EventType, ActorName, PayloadJson)
                VALUES (@sessionId, @eventType, @actorName, @payloadJson)";

            using var command = new MySqlCommand(insertSql, connection, transaction);
            command.Parameters.AddWithValue("@sessionId", sessionId);
            command.Parameters.AddWithValue("@eventType", eventType);
            command.Parameters.AddWithValue("@actorName", actorName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@payloadJson", payloadJson ?? (object)DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }
    }
}
