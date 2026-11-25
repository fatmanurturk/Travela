using System.Text.Json;
using PdfSharp.Pdf.IO;
using Travela.Api.Documents;
using Travela.DTOs;

var root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var output = Path.Combine(root, "output/pdf");
var qa = Path.Combine(root, "tmp/pdfs/layout-qa");
Directory.CreateDirectory(output);
Directory.CreateDirectory(qa);
var renderer = new TicketPdfRenderer();
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
var manifest = new List<object>();

PurchaseDto Ticket() => new()
{
    Kind = "Ticket", Pnr = "8A46CF2390B74D68BFFE1234A56789BC", PaymentStatus = "Paid", TicketStatus = "Active",
    PassengerName = "Çağrı Şükrü Işık Öztürk", CompanyName = "Travela Seyahat",
    DepartureCity = "İzmir", ArrivalCity = "Şanlıurfa", SeatNumber = 17,
    DepartureTime = new DateTime(2026, 12, 18, 6, 30, 0, DateTimeKind.Utc),
    ArrivalTime = new DateTime(2026, 12, 19, 0, 45, 0, DateTimeKind.Utc),
    CancelDeadline = new DateTime(2026, 12, 18, 4, 30, 0, DateTimeKind.Utc),
    RetrievedAt = new DateTime(2026, 11, 25, 11, 30, 0, DateTimeKind.Utc),
    Amount = 1200, OriginalAmount = 1500, DiscountAmount = 300, Currency = "TRY",
    CampaignName = "Kasım seyahat indirimi", CouponCode = "YOLCULUK", RefundStatus = "None", CanCancel = true,
    DepartureTerminalName = "İzmir Şehirlerarası Otobüs Terminali",
    DepartureTerminalAddress = "Karacaoğlan Mahallesi, 6170 Sokak No: 1, Bornova / İzmir",
    ArrivalTerminalName = "Şanlıurfa Şehirlerarası Otobüs Terminali",
    ArrivalTerminalAddress = "Esentepe Mahallesi, Şanlıurfa-Diyarbakır Yolu, Haliliye / Şanlıurfa",
    BoardingInstructions = "Peron bilgisini hareketten önce firma görevlisinden öğreniniz."
};

void Generate(string name, PurchaseDto ticket, bool singlePage, string? destination = null)
{
    var path = destination ?? Path.Combine(qa, name + ".pdf");
    File.WriteAllBytes(path, renderer.Render(ticket));
    using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    if (singlePage && pdf.PageCount != 1) throw new Exception(name + ": normal içerik tek sayfaya sığmadı");
    if (name == "overflow" && pdf.PageCount <= 1) throw new Exception("Uzun içerik için sayfa devamı oluşmadı");
    foreach (var page in pdf.Pages)
        if (Math.Abs(page.Width.Point - 595.276) > 1 || Math.Abs(page.Height.Point - 841.89) > 1)
            throw new Exception(name + ": A4 sayfa ölçüleri hatalı");
    manifest.Add(new { name, path, pages = pdf.PageCount, ticket });
    Console.WriteLine($"OK: {name}, {pdf.PageCount} A4 sayfa");
}

Generate("normal", Ticket(), true, Path.Combine(output, "travela-bilet-detayi-ornek.pdf"));
foreach (var status in new[] { "Succeeded", "Pending", "Failed" })
{
    var cancelled = Ticket();
    cancelled.TicketStatus = "Cancelled";
    cancelled.RefundStatus = status;
    cancelled.RefundAmount = cancelled.Amount;
    cancelled.CancelledAt = cancelled.RetrievedAt.AddMinutes(-20);
    cancelled.CancellationReason = "Yolcunun seyahat planı değişikliği";
    cancelled.LastError = status == "Failed" ? "Sağlayıcı yanıtı alınamadı; işlem takip ediliyor." : null;
    Generate("refund-" + status.ToLowerInvariant(), cancelled, true);
}
var changed = Ticket();
changed.ChangeCancellationUntil = changed.DepartureTime.AddHours(-1);
Generate("changed-trip", changed, true);
var expired = Ticket();
expired.CanCancel = false;
expired.DiscountAmount = 0;
Generate("expired", expired, true);
var missing = Ticket();
missing.DepartureTerminalName = missing.ArrivalTerminalName = null;
missing.DepartureTerminalAddress = missing.ArrivalTerminalAddress = missing.BoardingInstructions = null;
Generate("no-terminals", missing, true);
var longNames = Ticket();
longNames.PassengerName = "Çağrı Şükrü Işık " + new string('Ğ', 44) + " Öztürk Çalışkan";
longNames.CompanyName = "Güneydoğu Anadolu Uluslararası Şehirlerarası Turizm ve Seyahat İşletmeleri";
longNames.Pnr = new string('F', 128);
longNames.DepartureCity = "Afyonkarahisar / Şuhut";
longNames.ArrivalCity = "Kahramanmaraş / Dulkadiroğlu";
Generate("long-fields", longNames, false);
var overflow = Ticket();
overflow.PassengerName = string.Join(" ", Enumerable.Repeat("Çağrı Şükrü Işık Öztürk", 60));
overflow.CompanyName = string.Join(" ", Enumerable.Repeat("Şehirlerarası Turizm ve Seyahat", 60));
overflow.DepartureTerminalAddress = string.Join(" ", Enumerable.Repeat("İstiklal Caddesi No: 123 Çankaya / Ankara", 45));
overflow.ArrivalTerminalAddress = new string('Ğ', 1800);
overflow.BoardingInstructions = string.Join("\n", Enumerable.Range(1, 45).Select(i => $"{i}. Biniş açıklaması: Şoför ve firma görevlisinden peron bilgisi alınız."));
overflow.TicketStatus = "Cancelled";
overflow.RefundStatus = "Failed";
overflow.RefundAmount = overflow.Amount;
overflow.CancelledAt = overflow.RetrievedAt;
overflow.CancellationReason = string.Join("\n", Enumerable.Repeat("Yolcu talebiyle iptal edildi. Çığ Şükrü Işık, özgün kayıt korunur.", 50));
overflow.LastError = new string('Ş', 1500);
Generate("overflow", overflow, false);

foreach (var invalid in new[] { "unpaid", "legacy", "no-pnr" })
{
    var ticket = Ticket();
    if (invalid == "unpaid") ticket.PaymentStatus = "Pending";
    if (invalid == "legacy") ticket.Kind = "Legacy";
    if (invalid == "no-pnr") ticket.Pnr = null;
    try { renderer.Render(ticket); throw new Exception("Belge üretim kuralı atlandı: " + invalid); }
    catch (InvalidOperationException) { Console.WriteLine("OK: geçersiz belge reddedildi: " + invalid); }
}
File.WriteAllText(Path.Combine(qa, "manifest.json"), JsonSerializer.Serialize(manifest, json));

// Re-render the existing API snapshots, when available, for the original contract checks.
foreach (var state in new[] { "active", "cancelled", "pending", "failed" })
{
    var snapshot = Path.Combine(root, "tmp/ticket-qa", state + ".json");
    if (!File.Exists(snapshot)) continue;
    var ticket = JsonSerializer.Deserialize<PurchaseDto>(File.ReadAllText(snapshot), json)!;
    var path = state is "active" or "cancelled" ? Path.Combine(output, "ticket-" + state + "-test.pdf")
        : Path.Combine(root, "tmp/ticket-qa/ticket-refund-" + state + ".pdf");
    File.WriteAllBytes(path, renderer.Render(ticket));
}
