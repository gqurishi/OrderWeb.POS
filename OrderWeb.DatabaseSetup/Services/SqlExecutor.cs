using System.Text;
using MySqlConnector;

namespace OrderWeb.DatabaseSetup.Services;

public static class SqlExecutor
{
    public static async Task ExecuteScriptAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken = default)
    {
        foreach (var statement in SplitSqlStatements(sql))
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            await using var command = new MySqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static IEnumerable<string> SplitSqlStatements(string sql)
    {
        var builder = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var current = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (current == '-' && next == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (current == '#' )
                {
                    inLineComment = true;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                builder.Append(current);
                continue;
            }

            if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                builder.Append(current);
                continue;
            }

            if (current == ';' && !inSingleQuote && !inDoubleQuote)
            {
                var statement = builder.ToString().Trim();
                if (statement.Length > 0)
                {
                    yield return statement;
                }

                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        var tail = builder.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    public static async Task<bool> TableExistsAsync(MySqlConnection connection, string tableName, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    public static async Task TestConnectionAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
