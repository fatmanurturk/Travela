using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Travela.Data;

internal static class SeatRulesMigration
{
    // Capture every old column, rather than loading the new EF model before the schema exists.
    public static async Task Apply(TravelaDbContext db)
    {
        const string migration = "20261110061925_AddPassengerGenderAndRouteGeometry";
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length == 0) { Console.WriteLine("Bekleyen migration yok; veri değiştirilmedi."); return; }
        if (!pending.SequenceEqual([migration])) throw new InvalidOperationException("Bekleyen migration listesi beklenenden farklı; işlem yapılmadı.");
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();
        var queries = new List<(string Table, string Query)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT t.name, STRING_AGG(CONVERT(nvarchar(max), QUOTENAME(c.name)), ',') WITHIN GROUP (ORDER BY c.column_id) FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id WHERE t.schema_id=SCHEMA_ID('dbo') AND t.name <> '__EFMigrationsHistory' GROUP BY t.name ORDER BY t.name";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0); var escaped = table.Replace("]", "]]");
                queries.Add((table, $"SELECT {reader.GetString(1)} FROM [dbo].[{escaped}] ORDER BY (SELECT NULL) FOR JSON PATH, INCLUDE_NULL_VALUES"));
            }
        }
        async Task<Dictionary<string, string>> Snapshot()
        {
            var snapshot = new Dictionary<string, string>();
            foreach (var (table, query) in queries)
            {
                using var command = connection.CreateCommand(); command.CommandText = query;
                using var reader = await command.ExecuteReaderAsync();
                var json = new StringBuilder(); while (await reader.ReadAsync()) json.Append(reader.GetString(0));
                // Sort serialized rows so a different SQL query plan does not change the fingerprint.
                using var document = System.Text.Json.JsonDocument.Parse(json.Length == 0 ? "[]" : json.ToString());
                var rows = document.RootElement.EnumerateArray().Select(r => r.GetRawText()).OrderBy(r => r, StringComparer.Ordinal);
                snapshot[table] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows))));
            }
            return snapshot;
        }
        var before = await Snapshot();
        await db.Database.MigrateAsync();
        var after = await Snapshot();
        if (before.Any(pair => after[pair.Key] != pair.Value)) throw new InvalidOperationException("Mevcut veri parmak izleri farklı. Eş zamanlı işlemleri inceleyiniz.");
        Console.WriteLine($"Migration uygulandı; {before.Count} tablonun tüm eski sütunlarındaki veriler SHA-256 karşılaştırmasıyla korundu. Eski cinsiyet ve geometri kayıtları NULL bırakıldı.");
    }
}
