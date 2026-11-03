namespace Travela.Api.Payments;

// Presentation only: never rewrites cancellation history or payment/refund state.
// Both the API response and PDF consume this same policy.
public static class RefundPresentation
{
    public static string Label(string status) => status switch
    {
        "Succeeded" => "Tam iade tamamlandı",
        "Pending" => "İade Bekleniyor",
        "Failed" => "İade Başarısız",
        _ => "İade yok"
    };

    public static string Describe(string status, bool isTest, string? error, bool needsAttention)
    {
        var explanation = status switch
        {
            "Succeeded" => "Tam iade tamamlandı.",
            "Pending" => "İade talebi kaydedildi, sonuç bekleniyor.",
            "Failed" => "İade başarısız; iade tamamlanmadı. " +
                (string.IsNullOrWhiteSpace(error) ? "Sağlayıcıdan başarılı iade sonucu alınamadı." : error.Trim().Replace("Yerel test adaptörü iade hatası.", "İade işlemi tamamlanamadı.")) +
                (needsAttention ? " Destek takibi gerekiyor." : " Güvenli yeniden deneme planlandı."),
            _ => "İade işlemi yok."
        };
        return explanation;
    }
}
