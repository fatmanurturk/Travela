using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Travela.Tests;

// Linked only into test executables. Application and migration connections are unchanged.
internal sealed class SqlTestDatabase
{
    private static readonly string[] AllowedPrefixes =
    [
        "TravelaPaymentTests_",
        "TravelaRouteTests_",
        "TravelaReportingTests_"
    ];

    public string Name { get; }
    public string ConnectionString { get; }

    private SqlTestDatabase(string name, string connectionString)
    {
        Name = name;
        ConnectionString = connectionString;
    }

    public static SqlTestDatabase Create(string? configuredConnection, string prefix)
    {
        if (!AllowedPrefixes.Contains(prefix, StringComparer.Ordinal))
            throw new InvalidOperationException("İzin verilen bir test veritabanı öneki gereklidir.");
        if (string.IsNullOrWhiteSpace(configuredConnection))
            throw new InvalidOperationException("SQL test bağlantısı bulunamadı.");

        var connection = new SqlConnectionStringBuilder(configuredConnection);
        if (string.IsNullOrWhiteSpace(connection.DataSource))
            throw new InvalidOperationException("SQL test sunucusu belirtilmelidir.");
        if (connection.TrustServerCertificate && !IsLocalServer(connection.DataSource))
            throw new InvalidOperationException(
                "Uzak test sunucusunda sertifika doğrulaması kapatılamaz. " +
                "Geçerli sertifika ve TrustServerCertificate=false kullanın.");

        var name = prefix + Guid.NewGuid().ToString("N");
        connection.InitialCatalog = name;
        // Never solve native SNI/Windows access errors by disabling TLS.
        if (connection.Encrypt != SqlConnectionEncryptOption.Strict)
            connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
        connection.ApplicationName = prefix.TrimEnd('_');
        connection.PersistSecurityInfo = false;
        var database = new SqlTestDatabase(name, connection.ConnectionString);
        database.AssertTarget(database.ConnectionString);
        return database;
    }

    internal static bool IsLocalServer(string server)
    {
        foreach (var protocol in new[] { "tcp:", "np:", "lpc:" })
        {
            if (server.StartsWith(protocol, StringComparison.OrdinalIgnoreCase))
            {
                server = server[protocol.Length..];
                break;
            }
        }
        var host = server.Split('\\')[0].Split(',')[0].Trim().Trim('[', ']');
        return new[] { ".", "(local)", "(localdb)", "localhost", "127.0.0.1", "::1", Environment.MachineName }
            .Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    public void AssertTarget(string? actualConnection)
    {
        var expected = new SqlConnectionStringBuilder(ConnectionString);
        var actual = new SqlConnectionStringBuilder(actualConnection);
        var prefix = AllowedPrefixes.FirstOrDefault(value => Name.StartsWith(value, StringComparison.Ordinal));
        if (prefix == null || !Guid.TryParseExact(Name[prefix.Length..], "N", out _) ||
            !string.Equals(actual.InitialCatalog, Name, StringComparison.Ordinal) ||
            !string.Equals(actual.DataSource, expected.DataSource, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "SQL test hedefi değişti. Ana veya başka bir veritabanına yazma/silme işlemi engellendi.");
    }

    public async Task VerifyServerAsync()
    {
        AssertTarget(ConnectionString);
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (SqlException error) when (error.Number == 20)
        {
            throw new InvalidOperationException(
                "SQL şifreleme/Windows kimlik doğrulaması bu çalıştırma ortamında kullanılamıyor. " +
                "scripts/test-stage0.ps1 dosyasını SQL erişimi olan normal yerel PowerShell " +
                "oturumunda çalıştırın; Codex sandbox kullanıyorsa yalnızca bu test komutuna " +
                "sandbox dışında çalışma izni verin. Encrypt=false kullanmayın. " +
                "Test veritabanı henüz oluşturulmadı.", error);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), encrypt_option
            FROM sys.dm_exec_connections WHERE session_id = @@SPID
            """;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetString(1) != "TRUE")
            throw new InvalidOperationException("SQL test bağlantısının şifreli olduğu doğrulanamadı.");

        Console.WriteLine($"SQL test hedefi: {reader.GetString(0)} / {Name}; şifreleme: TRUE.");
        Console.WriteLine("Yazma ve temizlik yalnızca bu çalıştırmaya ait geçici veritabanında yapılır.");
    }

    public async Task DeleteAsync(DbContext db)
    {
        AssertTarget(db.Database.GetConnectionString());
        if (!string.Equals(db.Database.GetDbConnection().Database, Name, StringComparison.Ordinal))
            throw new InvalidOperationException("Açık bağlantının veritabanı değişti; temizlik engellendi.");
        SqlConnection.ClearAllPools();
        await db.Database.EnsureDeletedAsync();
        Console.WriteLine($"SQL test veritabanı temizlendi: {Name}");
    }
}
