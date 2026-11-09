using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travela.Api.Payments;
using Travela.Data;

// Every HTTP request ends at StubHandler. No web host, database connection,
// external API, secrets store, or persistent Data Protection key ring is used.
var passed = 0;
using var db = new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().Options);
var protection = new EphemeralDataProtectionProvider();
foreach (var sandbox in new[] { true, false })
{
    var settings = new IyzicoOptions
    {
        ApiKey = (sandbox ? "sandbox-" : "") + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        SecretKey = (sandbox ? "sandbox-" : "") + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        CallbackBaseUrl = "https://callback.example.test"
    };
    var options = Options.Create(settings);
    using var handler = new StubHandler();
    using var http = new HttpClient(handler);
    IyzicoProviderBase provider = sandbox
        ? new IyzicoSandboxProvider(http, db, options, protection)
        : new IyzicoProvider(http, db, options, protection);
    Check(provider.Name == (sandbox ? "IyzicoSandbox" : "Iyzico") && provider.IsTest == sandbox,
        "Sağlayıcı kimliği ve test bilgisi ortama uygun");

    Func<string?, bool> allowed = sandbox
        ? IyzicoSandboxProvider.AllowedCheckoutUrl
        : IyzicoProvider.AllowedCheckoutUrl;
    var apiHost = sandbox ? "sandbox-api.iyzipay.com" : "api.iyzipay.com";
    var checkoutHost = sandbox ? "sandbox-cpp.iyzipay.com" : "cpp.iyzipay.com";
    var otherApiHost = sandbox ? "api.iyzipay.com" : "sandbox-api.iyzipay.com";
    var otherCheckoutHost = sandbox ? "cpp.iyzipay.com" : "sandbox-cpp.iyzipay.com";
    Check(allowed($"https://{apiHost}/checkoutform/session"), "Ortamın API checkout adresi kabul edilir");
    Check(allowed($"https://{checkoutHost}/?token=session"), "Ortamın ortak ödeme sayfası kabul edilir");
    foreach (var url in new string?[]
    {
        null,
        $"https://{otherApiHost}/checkoutform/session",
        $"https://{otherCheckoutHost}/?token=session",
        $"http://{checkoutHost}/?token=session",
        $"https://{checkoutHost}.evil.test/?token=session",
        $"https://{checkoutHost}@evil.test/?token=session",
        $"https://user:password@{checkoutHost}/?token=session",
        $"https://{checkoutHost}:444/?token=session",
        $"https://{checkoutHost}/other?token=session",
        $"https://{checkoutHost}/?notatoken=session",
        $"https://{checkoutHost}/?token=",
        $"https://{checkoutHost}/?token=one&token=two",
        $"https://{checkoutHost}/?token=session#fragment",
        $"//{checkoutHost}/?token=session",
        "javascript:alert(1)"
    })
        Check(!allowed(url), "Yanlış ortam veya güvensiz checkout adresi reddedilir");

    var notice = new IyzicoNotice
    {
        IyziEventType = "CHECKOUT_FORM_AUTH",
        IyziPaymentId = JsonSerializer.SerializeToElement(12345),
        Token = Guid.NewGuid().ToString("N"),
        PaymentConversationId = Guid.NewGuid().ToString("N"),
        Status = "SUCCESS"
    };
    var signature = Sign(settings.SecretKey, settings.SecretKey + notice.IyziEventType +
        notice.IyziPaymentId + notice.Token + notice.PaymentConversationId + notice.Status);
    Check(provider.VerifyWebhook(notice, signature), "Sayısal paymentId içeren V3 HPP imzası doğrulanır");
    notice.IyziPaymentId = JsonSerializer.SerializeToElement("12345");
    Check(provider.VerifyWebhook(notice, signature), "Metin paymentId aynı V3 sözleşmesiyle doğrulanır");
    foreach (var invalid in new[] { "", "not-hex", signature[..^2], new string('0', 64) })
        Check(!provider.VerifyWebhook(notice, invalid), "Eksik veya bozuk webhook imzası reddedilir");
    Check(!provider.VerifyWebhook(notice, Sign(settings.ApiKey, settings.ApiKey + notice.IyziEventType +
        notice.IyziPaymentId + notice.Token + notice.PaymentConversationId + notice.Status)),
        "Farklı anahtarla imzalanmış webhook reddedilir");
    notice.Status = "FAILURE";
    Check(!provider.VerifyWebhook(notice, signature), "Değiştirilmiş webhook durumu reddedilir");
    notice.Status = "SUCCESS";
    notice.Token += "changed";
    Check(!provider.VerifyWebhook(notice, signature), "Değiştirilmiş webhook token'ı reddedilir");
    notice.IyziEventType = "API_AUTH";
    var wrongEventSignature = Sign(settings.SecretKey, settings.SecretKey + notice.IyziEventType +
        notice.IyziPaymentId + notice.Token + notice.PaymentConversationId + notice.Status);
    Check(!provider.VerifyWebhook(notice, wrongEventSignature), "HPP dışındaki olay türü reddedilir");
    Check(!provider.Verify(new ProviderNotice(Guid.NewGuid(), "event", 0), signature),
        "Yerel test bildirim protokolü iyzico tarafından kabul edilmez");

    var probe = new ProtocolProbe(http, db, options, protection, sandbox);
    handler.Respond = async request =>
    {
        var body = await request.Content!.ReadAsStringAsync();
        var random = request.Headers.GetValues("x-iyzi-rnd").Single();
        var auth = request.Headers.GetValues("Authorization").Single();
        var expected = "apiKey:" + settings.ApiKey + "&randomKey:" + random + "&signature:" +
            Sign(settings.SecretKey, random + request.RequestUri!.AbsolutePath + body);
        Check(request.RequestUri.Host == apiHost, "HTTP isteği seçilen ortam adresini kullanır");
        Check(auth.StartsWith("IYZWSv2 ", StringComparison.Ordinal) &&
            Encoding.UTF8.GetString(Convert.FromBase64String(auth[8..])) == expected,
            "İstek imzası yol, rastgele değer ve gönderilen JSON ile eşleşir");
        Check(random.Length == 32, "Her isteğin rastgele imza değeri bulunur");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"success\"}")
        };
    };
    await probe.Send(HttpMethod.Post, "/payment/iyzipos/checkoutform/initialize/auth/ecom",
        new { price = "42.5", currency = "TRY" });
    await probe.Send(HttpMethod.Post, "/payment/iyzipos/checkoutform/auth/ecom/detail",
        new { token = "test-session" });
    await probe.Send(HttpMethod.Get, "/v2/reporting/payment/details?locale=tr&paymentId=test", null);
    await probe.Send(HttpMethod.Post, "/payment/refund", new { price = "42.5", currency = "TRY" });
    Check(handler.Calls == 4, "Başlatma, sorgu, rapor ve iade istekleri yalnızca sahte HTTP taşımasına gider");

    foreach (var values in new[]
    {
        new[] { "conversation", "token" },
        new[] { "SUCCESS", "payment", "TRY", "basket", "conversation", "42.5", "42.5", "token" },
        new[] { "payment", "42.5", "TRY", "refund-conversation" }
    })
    {
        var signed = JsonSerializer.SerializeToElement(new
        {
            signature = Sign(settings.SecretKey, string.Join(':', values))
        });
        probe.CheckResponse(signed, values);
        Check(true, "Başlatma, sorgu veya iade yanıtının imzası doğrulanır");
        var changed = values.ToArray();
        changed[0] += "changed";
        await Reject<PaymentFlowException>(() =>
        {
            probe.CheckResponse(signed, changed);
            return Task.CompletedTask;
        }, e => e.Status == 502, "İmzalı yanıtın değiştirilmiş alanı reddedilir");
    }
    await Reject<PaymentFlowException>(() =>
    {
        probe.CheckResponse(JsonSerializer.SerializeToElement(new { status = "success" }), "value");
        return Task.CompletedTask;
    }, e => e.Status == 502, "İmzasız sağlayıcı yanıtı reddedilir");

    handler.Respond = _ => throw new TaskCanceledException();
    await Reject<PaymentFlowException>(() => probe.Send(HttpMethod.Post, "/payment/refund", null),
        e => e.Status == 503, "HTTP zaman aşımı doğrulanmış ödeme veya iadeye dönüşmez");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Reject<OperationCanceledException>(() =>
        probe.Send(HttpMethod.Post, "/payment/refund", null, cancelled.Token),
        _ => true, "Çağıranın iptali korunur");

    var invalidOptions = Options.Create(new IyzicoOptions());
    var unconfigured = new ProtocolProbe(http, db, invalidOptions, protection, sandbox);
    var calls = handler.Calls;
    await Reject<PaymentFlowException>(() => unconfigured.Send(HttpMethod.Post, "/payment/refund", null),
        e => e.Status == 503, "Eksik ayarlarla HTTP isteği gönderilmez");
    Check(handler.Calls == calls, "Eksik yapılandırma HTTP taşımasına ulaşmaz");
}

var culture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
    Check(IyzicoProviderBase.Money(1234.50m) == "1234.5", "Tutar imzası Türkçe kültürden etkilenmez");
}
finally
{
    CultureInfo.CurrentCulture = culture;
}
Console.WriteLine($"PASS: {passed} iyzico sağlayıcı kontrolü; dış ağ ve veritabanı kullanılmadı.");

void Check(bool condition, string name)
{
    if (!condition)
        throw new Exception(name);
    passed++;
}

async Task Reject<T>(Func<Task> action, Func<T, bool> matches, string name) where T : Exception
{
    try
    {
        await action();
    }
    catch (T error) when (matches(error))
    {
        Check(true, name);
        return;
    }
    throw new Exception(name);
}

static string Sign(string key, string value) => Convert.ToHexStringLower(HMACSHA256.HashData(
    Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value)));

sealed class StubHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } =
        _ => throw new InvalidOperationException("Test HTTP yanıtı ayarlanmadı.");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        return Respond(request);
    }
}

sealed class ProtocolProbe(
    HttpClient http,
    TravelaDbContext db,
    IOptions<IyzicoOptions> options,
    IDataProtectionProvider protection,
    bool sandbox) : IyzicoProviderBase(http, db, options, protection)
{
    public override string Name => sandbox ? "IyzicoSandbox" : "Iyzico";
    public override bool IsTest => sandbox;
    protected override string ApiBaseUrl => sandbox ? IyzicoSandboxProvider.ApiBase : IyzicoProvider.ApiBase;
    protected override string CheckoutPageHost => sandbox ? "sandbox-cpp.iyzipay.com" : "cpp.iyzipay.com";

    public Task<JsonElement> Send(HttpMethod method, string path, object? body, CancellationToken ct = default) =>
        Call(method, path, body, ct);

    public void CheckResponse(JsonElement json, params string[] values) => VerifyResponse(json, values);
}
