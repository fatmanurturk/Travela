// Hukuki inceleme gerektirir. Versioned draft snapshots are shared with the frontend.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.DTOs;

namespace Travela.Api.Legal;

public static class LegalDocuments
{
    public const string Privacy = "PrivacyNotice";
    public const string Terms = "TermsOfUse";
    public const string Sales = "DistanceSales";

    private static readonly JsonElement Catalog = Load();
    public static string Version => Catalog.GetProperty("version").GetString()!;
    public static JsonElement Archive => Catalog;

    private static JsonElement Load()
    {
        using var stream = typeof(LegalDocuments).Assembly.GetManifestResourceStream("Travela.Legal.documents.v1.json")
            ?? throw new InvalidOperationException("Hukuki belge arşivi bulunamadı.");
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.Clone();
    }

    public static void Validate(IReadOnlyList<LegalAcceptanceDto>? acceptances, params string[] required)
    {
        if (acceptances is null || acceptances.Count != required.Length ||
            acceptances.Any(a => a is null || !a.Accepted) ||
            acceptances.Select(a => a.DocumentType).Distinct().Count() != required.Length ||
            required.Any(type => !acceptances.Any(a => a.DocumentType == type)))
            throw new PaymentFlowException("Gerekli hukuki metinleri okuyup ilgili onay kutularını işaretleyin.", 400);

        if (acceptances.Any(a => a.Version != Version))
            throw new PaymentFlowException("Hukuki metinlerin sürümü değişti. Sayfayı yenileyip güncel metinleri inceleyin.", 409);
    }

    public static string RequireIp(string? ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var address))
            throw new PaymentFlowException("Onay kaydı için istemci adresi alınamadı. Lütfen tekrar deneyin.", 400);
        return address.ToString();
    }

    public static LegalAcceptance Record(string type, DateTime now, string ip)
    {
        var document = Catalog.GetProperty("documents").EnumerateArray()
            .Single(d => d.GetProperty("type").GetString() == type);
        var snapshot = JsonSerializer.Serialize(new
        {
            notice = Catalog.GetProperty("notice").GetString(),
            version = Version,
            updatedAt = Catalog.GetProperty("updatedAt").GetString(),
            document
        });
        return new LegalAcceptance
        {
            DocumentType = type,
            DocumentVersion = Version,
            DocumentSnapshot = snapshot,
            DocumentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))),
            AcceptedAt = DateTime.SpecifyKind(now, DateTimeKind.Utc),
            IpAddress = RequireIp(ip)
        };
    }
}
