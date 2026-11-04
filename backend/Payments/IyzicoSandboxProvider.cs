using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travela.Api.Models;
using Travela.Data;
using Travela.DTOs;

namespace Travela.Api.Payments;

public sealed class IyzicoOptions
{
    public string ApiKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string CallbackBaseUrl { get; set; } = "";
    public bool Valid => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SecretKey) && Uri.TryCreate(CallbackBaseUrl, UriKind.Absolute, out var u) && u.Scheme == "https" && string.IsNullOrEmpty(u.UserInfo) && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment);
}
public record IyzicoBuyer(CheckoutBuyerDto Buyer, string Email, string Ip);
public sealed class IyzicoNotice
{
    public string IyziEventType { get; set; } = "";
    public JsonElement IyziPaymentId { get; set; }
    public string Token { get; set; } = "";
    public string PaymentConversationId { get; set; } = "";
    public string Status { get; set; } = "";
    public string IyziReferenceCode { get; set; } = "";
}

public sealed class IyzicoSandboxProvider(HttpClient http, TravelaDbContext db, IOptions<IyzicoOptions> options, IDataProtectionProvider protection) : IPaymentProvider
{
    public string Name => "IyzicoSandbox";
    public bool IsTest => true;
    public const string ApiBase = "https://sandbox-api.iyzipay.com";
    public static string Money(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string Hmac(string value) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.SecretKey), Encoding.UTF8.GetBytes(value)));
    public static bool FixedEquals(string a, string b)
    {
        try { return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a), Convert.FromHexString(b)); } catch (FormatException) { return false; }
    }
    public static bool AllowedCheckoutUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Port == 443 && string.IsNullOrEmpty(u.UserInfo) &&
        ((u.Host == "sandbox-api.iyzipay.com" && u.AbsolutePath.StartsWith("/checkoutform/", StringComparison.Ordinal)) || (u.Host == "sandbox-cpp.iyzipay.com" && u.AbsolutePath == "/" && u.Query.Contains("token=", StringComparison.Ordinal)));
    private static string Value(JsonElement json, string name) => json.GetProperty(name).ToString();
    private static decimal Amount(JsonElement json, string name) => decimal.Parse(Value(json, name), CultureInfo.InvariantCulture);
    private void VerifyResponse(JsonElement json, params string[] values)
    {
        if (!json.TryGetProperty("signature", out var signature) || !FixedEquals(Hmac(string.Join(':', values)), signature.GetString() ?? ""))
            throw new PaymentFlowException("iyzico yanıt imzası doğrulanamadı; işlem sonucu değiştirilmedi.", 502);
    }
    public bool Verify(ProviderNotice notice, string signature) => false;
    public bool VerifyWebhook(IyzicoNotice notice, string signature) => options.Value.Valid && notice.IyziEventType == "CHECKOUT_FORM_AUTH" &&
        FixedEquals(Hmac(options.Value.SecretKey + notice.IyziEventType + notice.IyziPaymentId + notice.Token + notice.PaymentConversationId + notice.Status), signature);

    public async Task<string> OpenCheckout(Guid orderId, decimal amount, string currency, CancellationToken ct)
    {
        if (!options.Value.Valid) throw new PaymentFlowException("iyzico sandbox anahtarları ve HTTPS dönüş adresi yapılandırılmalıdır.", 503);
        var group = await db.PurchaseGroups.Include(g => g.Items).ThenInclude(p => p.Reservation).SingleAsync(g => g.Id == orderId, ct);
        if (group.ProviderToken != null && AllowedCheckoutUrl(group.CheckoutUrl)) return group.CheckoutUrl!;
        var payload = JsonSerializer.Deserialize<IyzicoBuyer>(protection.CreateProtector("Travela.IyzicoBuyer.v1").Unprotect(group.ProtectedBuyerJson ?? throw new PaymentFlowException("Alıcı bilgileri bulunamadı.")), JsonOptions)!;
        var b = payload.Buyer;
        var address = new { contactName = b.Name + " " + b.Surname, city = b.City, country = b.Country, address = b.Address, zipCode = b.ZipCode };
        var result = await Call(HttpMethod.Post, "/payment/iyzipos/checkoutform/initialize/auth/ecom", new {
            locale = "tr", conversationId = group.Id.ToString("N"), price = Money(amount), paidPrice = Money(amount), currency, basketId = group.Id.ToString("N"), paymentGroup = "PRODUCT",
            callbackUrl = options.Value.CallbackBaseUrl.TrimEnd('/') + "/api/payments/iyzico/callback/" + group.Id, enabledInstallments = new[] { 1 },
            buyer = new { id = group.PassengerId.ToString(CultureInfo.InvariantCulture), name = b.Name, surname = b.Surname, gsmNumber = b.Phone, email = payload.Email, identityNumber = b.IdentityNumber,
                registrationAddress = b.Address, city = b.City, country = b.Country, zipCode = b.ZipCode, ip = payload.Ip }, shippingAddress = address, billingAddress = address,
            basketItems = group.Items.OrderBy(p => p.Id).Select(p => new { id = p.Id.ToString("N"), name = "Otobüs bileti - Koltuk " + p.Reservation.SeatNumber, category1 = "Travel", itemType = "VIRTUAL", price = Money(p.Amount) }).ToArray()
        }, ct);
        if (Value(result, "status") != "success") throw new PaymentFlowException("iyzico ödeme sayfasını başlatamadı. Alıcı bilgilerini ve sandbox ayarlarını kontrol edin.", 502);
        var token = Value(result, "token"); VerifyResponse(result, Value(result, "conversationId"), token);
        if (Value(result, "conversationId") != group.Id.ToString("N") || token.Length is < 16 or > 200 || !AllowedCheckoutUrl(Value(result, "paymentPageUrl"))) throw new PaymentFlowException("iyzico ödeme adresi veya sipariş kimliği doğrulanamadı.", 502);
        group.ProviderToken = token; group.CheckoutUrl = Value(result, "paymentPageUrl"); group.ProtectedBuyerJson = null;
        group.CheckoutState = "Ready"; group.CheckoutLeaseUntil = null;
        await db.SaveChangesAsync(ct); return group.CheckoutUrl;
    }

    public async Task<ProviderPayment> GetPayment(Guid orderId, CancellationToken ct)
    {
        var group = await db.PurchaseGroups.AsNoTracking().Include(g => g.Items).SingleAsync(g => g.Id == orderId, ct);
        if (group.ProviderToken == null) return new(orderId, "UNINITIALIZED", "Pending", group.Amount, group.Currency, true);
        var json = await Call(HttpMethod.Post, "/payment/iyzipos/checkoutform/auth/ecom/detail", new { locale = "tr", conversationId = orderId.ToString("N"), token = group.ProviderToken }, ct);
        // A failed inquiry is not proof of a failed payment.
        if (Value(json, "status") != "success") return new(orderId, "UNCONFIRMED", "Pending", group.Amount, group.Currency, true);
        VerifyResponse(json, Value(json, "paymentStatus"), Value(json, "paymentId"), Value(json, "currency"), Value(json, "basketId"), Value(json, "conversationId"), Money(Amount(json, "paidPrice")), Money(Amount(json, "price")), Value(json, "token"));
        if (Value(json, "token") != group.ProviderToken || Value(json, "conversationId") != orderId.ToString("N") || Value(json, "basketId") != orderId.ToString("N") || Amount(json, "price") != group.Amount || Amount(json, "paidPrice") != group.Amount || Value(json, "currency") != group.Currency)
            throw new PaymentFlowException("iyzico tahsilatı siparişle eşleşmiyor.", 502);
        var state = Value(json, "paymentStatus") switch { "SUCCESS" => "Succeeded", "FAILURE" => "Failed", _ => "Pending" };
        if (state == "Succeeded" && (!json.TryGetProperty("fraudStatus", out var fraud) || fraud.GetInt32() != 1)) state = "Pending";
        var lines = state == "Succeeded" ? json.GetProperty("itemTransactions").EnumerateArray().Select(i => new ProviderLine(Guid.Parse(Value(i, "itemId")), Value(i, "paymentTransactionId"), Amount(i, "paidPrice"))).ToList() : null;
        return new(orderId, Value(json, "paymentId"), state, Amount(json, "paidPrice"), Value(json, "currency"), true, lines);
    }

    public async Task<ProviderRefund> Refund(Guid orderId, decimal amount, string key, CancellationToken ct)
    {
        var line = await db.PaymentOrders.AsNoTracking().Include(p => p.PurchaseGroup).SingleAsync(p => p.Id == orderId, ct);
        if (line.PaymentStatus != "Paid" || line.Amount != amount || line.RefundKey != key || line.ProviderReference == null || line.PurchaseGroup?.ProviderPaymentId == null) return new("Failed", null, "İade satırı doğrulanamadı.");
        // Inquiry before every attempt; conversationId is correlation, not an idempotency guarantee.
        var report = await Call(HttpMethod.Get, "/v2/reporting/payment/details?locale=tr&paymentId=" + Uri.EscapeDataString(line.PurchaseGroup.ProviderPaymentId), null, ct);
        if (Value(report, "status") != "success") return new("Pending", null, "İade geçmişi sağlayıcıdan doğrulanamadı; yeniden para gönderilmedi.");
        var payments = report.GetProperty("payments").EnumerateArray().Where(p => Value(p, "paymentId") == line.PurchaseGroup.ProviderPaymentId && Value(p, "paymentConversationId") == line.PurchaseGroup.Id.ToString("N")).ToList();
        if (payments.Count != 1) return new("Pending", null, "Sağlayıcı ödeme geçmişi eşleşmiyor.");
        var items = payments[0].GetProperty("itemTransactions").EnumerateArray().Where(i => Value(i, "paymentTransactionId") == line.ProviderReference).ToList();
        if (items.Count != 1 || Amount(items[0], "paidPrice") != amount) return new("Failed", null, "İade işlem satırı veya tutarı eşleşmiyor.");
        var refunds = items[0].GetProperty("refunds").EnumerateArray().ToList();
        var recorded = refunds.Where(r => Value(r, "refundConversationId") == key).ToList();
        if (recorded.Count == 1 && Amount(recorded[0], "refundPrice") == amount && Value(recorded[0], "currencyCode") == line.Currency && Value(recorded[0], "refundStatus") == "1")
            return new("Succeeded", Value(recorded[0], "refundTxId"), null);
        var prior = await db.ProviderRefundAttempts.SingleOrDefaultAsync(a => a.OrderId == orderId, ct);
        if ((prior != null && prior.Status != "Retryable") || refunds.Count != 0) return new("Pending", null, "Önceki iade sonucu kesinleşmedi; çift iade yapılmadan mutabakat bekleniyor.");
        var attempt = prior ?? new ProviderRefundAttempt { OrderId = orderId, RefundKey = key }; if (prior == null) db.ProviderRefundAttempts.Add(attempt); attempt.Status = "Submitting"; await db.SaveChangesAsync(ct);
        try
        {
            var result = await Call(HttpMethod.Post, "/payment/refund", new { locale = "tr", conversationId = key, paymentTransactionId = line.ProviderReference, price = Money(amount), currency = line.Currency }, ct);
            if (Value(result, "status") != "success") { attempt.Status = result.TryGetProperty("retryable", out var retryable) && retryable.ValueKind == JsonValueKind.True ? "Retryable" : "Rejected"; await db.SaveChangesAsync(ct); return new("Failed", null, "iyzico iade talebini reddetti; sağlayıcı geçmişi kontrol edilecek."); }
            VerifyResponse(result, Value(result, "paymentId"), Money(Amount(result, "price")), Value(result, "currency"), Value(result, "conversationId"));
            if (Value(result, "paymentId") != line.PurchaseGroup.ProviderPaymentId || Value(result, "paymentTransactionId") != line.ProviderReference || Amount(result, "price") != amount || Value(result, "currency") != line.Currency || Value(result, "conversationId") != key) throw new PaymentFlowException("İade yanıtı eşleşmiyor.");
            attempt.Status = "Succeeded"; attempt.Reference = "IYZICO-REFUND-" + key; await db.SaveChangesAsync(ct);
            return new("Succeeded", attempt.Reference, null);
        }
        catch (Exception e) when (e is not OperationCanceledException) { attempt.Status = "Unknown"; await db.SaveChangesAsync(ct); return new("Pending", null, "İade yanıtı alınamadı; aynı iade sağlayıcıdan sorgulanacak, tekrar tahsil/iade edilmeyecek."); }
    }

    private async Task<JsonElement> Call(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!options.Value.Valid) throw new PaymentFlowException("iyzico sandbox yapılandırması eksik.", 503);
        var json = body == null ? "{}" : JsonSerializer.Serialize(body, JsonOptions); var random = Guid.NewGuid().ToString("N");
        var signature = Hmac(random + path.Split('?')[0] + json);
        using var request = new HttpRequestMessage(method, ApiBase + path);
        request.Headers.TryAddWithoutValidation("Authorization", "IYZWSv2 " + Convert.ToBase64String(Encoding.UTF8.GetBytes("apiKey:" + options.Value.ApiKey + "&randomKey:" + random + "&signature:" + signature)));
        request.Headers.TryAddWithoutValidation("x-iyzi-rnd", random);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PaymentFlowException("iyzico sorgusu zaman aşımına uğradı; sonuç daha sonra yeniden doğrulanacak.", 503); }
        using var responseScope = response; response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); return result.RootElement.Clone();
    }
}
