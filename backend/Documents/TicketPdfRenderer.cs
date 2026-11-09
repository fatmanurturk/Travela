using System.Globalization;
using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using Travela.DTOs;

namespace Travela.Api.Documents;

public sealed class TicketPdfRenderer
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo Turkey = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    static TicketPdfRenderer() { GlobalFontSettings.FontResolver ??= new TicketFontResolver(); }
    public static string TurkeyTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Turkey).ToString("dd MMMM yyyy HH:mm", Turkish);

    public byte[] Render(PurchaseDto ticket)
    {
        if (ticket.Kind != "Ticket" || ticket.PaymentStatus != "Paid" || string.IsNullOrWhiteSpace(ticket.Pnr))
            throw new InvalidOperationException("Ödemesi doğrulanmamış işlem için bilet belgesi üretilemez.");
        var cancelled = ticket.TicketStatus == "Cancelled";
        var document = new Document();
        document.Info.Title = "Travela - Bilet";
        document.Info.Author = "Travela";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Travela"; normal.Font.Size = 11;
        normal.Font.Color = Color.FromRgb(23, 43, 77);
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A5;
        section.PageSetup.TopMargin = Unit.FromMillimeter(13);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(16);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromMillimeter(12);
        section.PageSetup.HeaderDistance = Unit.FromMillimeter(4);
        section.PageSetup.FooterDistance = Unit.FromMillimeter(6);
        var logo = section.AddParagraph("Travela.");
        logo.Format.Font.Size = 22; logo.Format.Font.Bold = true; logo.Format.Font.Color = Color.FromRgb(29, 78, 216);
        logo.Format.SpaceAfter = Unit.FromPoint(8);
        if (cancelled)
        {
            var header = section.Headers.Primary.AddParagraph("İPTAL EDİLDİ");
            header.Format.Font.Size = 8; header.Format.Font.Bold = true;
            header.Format.Font.Color = Color.FromRgb(166, 38, 38); header.Format.SpaceAfter = 0;
            var banner = section.AddParagraph("İPTAL EDİLDİ");
            banner.Format.Font.Size = 22; banner.Format.Font.Bold = true;
            banner.Format.Font.Color = Color.FromRgb(166, 38, 38);
            banner.Format.Shading.Color = Color.FromRgb(255, 235, 235);
            banner.Format.SpaceAfter = Unit.FromPoint(12);
        }
        var route = section.AddParagraph(BreakLongWords($"{ticket.DepartureCity} → {ticket.ArrivalCity}"));
        route.Format.Font.Size = 17; route.Format.Font.Bold = true; route.Format.SpaceAfter = Unit.FromPoint(10);
        var pnr = section.AddParagraph("PNR  " + ticket.Pnr);
        // PNR is an identifier: keep all characters intact, without inserting hyphens.
        pnr.Format.Font.Size = 10; pnr.Format.Font.Bold = true; pnr.Format.SpaceAfter = Unit.FromPoint(10);
        var table = section.AddTable();
        table.Borders.Color = Color.FromRgb(225, 232, 242); table.Borders.Width = 0.5;
        table.AddColumn(Unit.FromMillimeter(39)); table.AddColumn(Unit.FromMillimeter(85));
        table.TopPadding = table.BottomPadding = Unit.FromPoint(3);
        table.LeftPadding = table.RightPadding = Unit.FromPoint(6);
        void Row(string label, string value)
        {
            var r = table.AddRow(); r.KeepWith = 0;
            r.Cells[0].Shading.Color = Color.FromRgb(245, 247, 251);
            var key = r.Cells[0].AddParagraph(label); key.Format.Font.Size = 10; key.Format.Font.Bold = true;
            key.Format.SpaceAfter = 0;
            r.Cells[1].AddParagraph(BreakLongWords(value)).Format.SpaceAfter = 0;
        }
        Row("Yolcu", ticket.PassengerName);
        Row("Firma", ticket.CompanyName);
        Row("Kalkış", TurkeyTime(ticket.DepartureTime));
        Row("Varış", TurkeyTime(ticket.ArrivalTime));
        if (!string.IsNullOrWhiteSpace(ticket.DepartureTerminalName)) Row("Kalkış terminali", ticket.DepartureTerminalName + "\n" + ticket.DepartureTerminalAddress);
        if (!string.IsNullOrWhiteSpace(ticket.ArrivalTerminalName)) Row("Varış terminali", ticket.ArrivalTerminalName + "\n" + ticket.ArrivalTerminalAddress);
        if (!string.IsNullOrWhiteSpace(ticket.BoardingInstructions)) Row("Biniş açıklaması", ticket.BoardingInstructions);
        Row("Koltuk", ticket.SeatNumber.ToString(Turkish));
        if (ticket.DiscountAmount > 0)
        {
            Row("İndirim öncesi", ticket.OriginalAmount.ToString("N2", Turkish) + " " + ticket.Currency);
            Row("Kampanya indirimi", "-" + ticket.DiscountAmount.ToString("N2", Turkish) + " " + ticket.Currency + "\n" + ticket.CampaignName + (ticket.CouponCode == null ? "" : " · " + ticket.CouponCode));
        }
        Row("Satın alma tutarı", ticket.Amount.ToString("N2", Turkish) + " " + ticket.Currency);
        Row("Bilet durumu", cancelled ? "İptal Edildi" : "Aktif");
        Row("Ödeme durumu", "Ödeme doğrulandı");
        Row("İade durumu", ticket.RefundStatusLabel);
        if (ticket.RefundStatus != "None") Row("İade tutarı", ticket.RefundAmount.ToString("N2", Turkish) + " " + ticket.Currency);
        if (cancelled && ticket.CancelledAt.HasValue) Row("İptal zamanı", TurkeyTime(ticket.CancelledAt.Value));
        if (!cancelled) Row("İptal son zamanı", TurkeyTime(ticket.CancelDeadline));
        var notes = section.AddParagraph(); notes.Format.SpaceBefore = Unit.FromPoint(12); notes.Format.Font.Size = 9;
        notes.AddText("Tüm saatler Türkiye saatidir (Europe/Istanbul). ");
        if (cancelled) notes.AddText("Bu bilet iptal edilmiştir; seyahat için kullanılamaz.");
        else notes.AddText(ticket.ChangeCancellationUntil.HasValue ? $"Sefer değişikliği nedeniyle {TurkeyTime(ticket.ChangeCancellationUntil.Value)} tarihine kadar tam iade hakkı tanımlanmıştır." : ticket.CanCancel ? "Kalkışa en az 2 saat kala tam iade talep edilebilir; tam 2 saatte iptal kabul edilir." : "İptal son zamanı geçtiğinden yolcu iptali ve iade yapılamaz.");
        if (!string.IsNullOrWhiteSpace(ticket.CancellationReason))
        {
            var history = section.AddParagraph(); history.Format.Font.Size = 9;
            history.AddFormattedText("İptal nedeni (geçmiş kayıt): ", TextFormat.Bold);
            history.AddText(BreakLongWords(ticket.CancellationReason));
        }
        var refund = section.AddParagraph(); refund.Format.Font.Size = 9;
        refund.AddFormattedText("Güncel iade açıklaması: ", TextFormat.Bold);
        refund.AddText(BreakLongWords(ticket.RefundDescription));
        section.AddParagraph("Bu belge üretildiği andaki durumu gösterir. Güncel durum için Biletlerim sayfasını kontrol ediniz.").Format.Font.Size = 8;
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8; footer.Format.Font.Color = Colors.Gray;
        footer.AddText("Durum bilgisi: " + TurkeyTime(ticket.RetrievedAt) + " (Türkiye)  |  ");
        footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream(); renderer.PdfDocument.Save(stream, false); return stream.ToArray();
    }
    // MigraDoc breaks at spaces/conditional hyphens; add break opportunities only to long words.
    // This changes presentation only, never the saved passenger name or cancellation history.
    private static string BreakLongWords(string text) => Regex.Replace(text, @"(\S{22})(?=\S)", "$1\u00AD");
}

internal sealed class TicketFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "Lato-Bold" : "Lato-Regular", false, italic);
    public byte[] GetFont(string faceName)
    {
        var assembly = typeof(TicketFontResolver).Assembly;
        using var font = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.Assets.Fonts.{faceName}.ttf")
            ?? throw new InvalidOperationException("Gömülü bilet yazı tipi bulunamadı.");
        using var bytes = new MemoryStream(); font.CopyTo(bytes); return bytes.ToArray();
    }
}
