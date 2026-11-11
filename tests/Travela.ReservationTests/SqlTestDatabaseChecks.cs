using Microsoft.Data.SqlClient;
using Travela.Tests;

internal static class SqlTestDatabaseChecks
{
    public static void Run()
    {
        const string source = "Server=localhost;Database=TravelaDB;Integrated Security=true;" +
            "Encrypt=false;TrustServerCertificate=true";
        var passed = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("BAŞARISIZ: " + message);
            passed++;
            Console.WriteLine("OK: " + message);
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException) { Check(true, message); return; }
            throw new Exception("BAŞARISIZ: " + message);
        }

        var database = SqlTestDatabase.Create(source, "TravelaPaymentTests_");
        var connection = new SqlConnectionStringBuilder(database.ConnectionString);
        Check(connection.InitialCatalog != "TravelaDB" &&
            database.Name.StartsWith("TravelaPaymentTests_", StringComparison.Ordinal),
            "Geliştirme veritabanı yerine yalnızca üretilmiş test hedefi kullanılır");
        Check(connection.Encrypt == SqlConnectionEncryptOption.Mandatory,
            "Encrypt=false test bağlantısına taşınmaz; TLS zorunludur");
        Check(connection.IntegratedSecurity && connection.TrustServerCertificate && !connection.PersistSecurityInfo,
            "Yerel Windows kimliği korunur; bağlantı gizli bilgi yayınlamaz");
        Check(new SqlConnectionStringBuilder(source).InitialCatalog == "TravelaDB",
            "Kaynak yapılandırma değiştirilmez");
        foreach (var prefix in new[] { "TravelaPaymentTests_", "TravelaRouteTests_", "TravelaReportingTests_" })
        {
            var first = SqlTestDatabase.Create(source, prefix);
            var second = SqlTestDatabase.Create(source, prefix);
            Check(first.Name != second.Name && Guid.TryParseExact(first.Name[prefix.Length..], "N", out _),
                prefix + " her çalıştırmada ayrı GUID üretir");
            first.AssertTarget(first.ConnectionString);
        }
        foreach (var name in new[] { "TravelaDB", "master", "model", "msdb", "tempdb", "TravelaPaymentTests_other" })
        {
            var unsafeConnection = new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = name };
            Reject(() => database.AssertTarget(unsafeConnection.ConnectionString),
                name + " üzerinde yazma/temizlik hedefi reddedilir");
        }
        var otherServer = new SqlConnectionStringBuilder(database.ConnectionString) { DataSource = "remote.example" };
        Reject(() => database.AssertTarget(otherServer.ConnectionString), "Sunucu değişikliği reddedilir");
        Reject(() => SqlTestDatabase.Create(source, "TravelaDB_"), "Bilinmeyen test öneki reddedilir");
        Reject(() => SqlTestDatabase.Create(null, "TravelaPaymentTests_"), "Eksik bağlantı reddedilir");
        Reject(() => SqlTestDatabase.Create("Database=TravelaDB", "TravelaPaymentTests_"), "Eksik sunucu reddedilir");
        Reject(() => SqlTestDatabase.Create(
            "Server=remote.example;Database=TravelaDB;TrustServerCertificate=true", "TravelaPaymentTests_"),
            "Uzak sunucuda sertifika doğrulamasını atlamak reddedilir");
        Check(!SqlTestDatabase.IsLocalServer("localhost.attacker.example") &&
            SqlTestDatabase.IsLocalServer("tcp:127.0.0.1,1433") &&
            SqlTestDatabase.IsLocalServer(Environment.MachineName + "\\TESTINSTANCE"),
            "Yerel sunucu denetimi ad öneki eşleşmesine güvenmez");
        var strict = SqlTestDatabase.Create(source + ";Encrypt=Strict", "TravelaPaymentTests_");
        Check(new SqlConnectionStringBuilder(strict.ConnectionString).Encrypt == SqlConnectionEncryptOption.Strict,
            "Strict şifreleme Mandatory seviyesine düşürülmez");
        Console.WriteLine($"SONUÇ: {passed} SQL test hedefi/şifreleme güvenlik kontrolü başarılı; veritabanına bağlanılmadı.");
    }
}
