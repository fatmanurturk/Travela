using System.Globalization;
using System.Text.RegularExpressions;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
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

        using var document = new PdfDocument();
        document.Info.Title = "Travela - Bilet detayı";
        document.Info.Author = "Travela";
        using var layout = new TicketLayout(document, ticket);
        layout.Render();
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    // All text remains native PDF text. Measurements and page breaks use the same embedded font.
    private sealed class TicketLayout : IDisposable
    {
        private const double Margin = 40, Padding = 12;
        private static readonly XColor Heading = XColor.FromArgb(66, 107, 150);
        private static readonly XColor Body = XColor.FromArgb(69, 87, 106);
        private static readonly XColor Muted = XColor.FromArgb(111, 122, 133);
        private static readonly XColor Rule = XColor.FromArgb(216, 224, 232);
        private static readonly XColor PaleBlue = XColor.FromArgb(241, 247, 253);
        private readonly PdfDocument document;
        private readonly PurchaseDto ticket;
        private readonly Dictionary<(double, bool), XFont> fonts = new();
        private readonly XImage panorama;
        private XGraphics gfx = null!;
        private PdfPage page = null!;
        private double y;
        private double Width => page.Width.Point - 2 * Margin;
        private double Bottom => page.Height.Point - 64;
        private bool Cancelled => ticket.TicketStatus == "Cancelled";
        private sealed record Line(string Text, XFont Font, XColor Color, double Height);

        public TicketLayout(PdfDocument document, PurchaseDto ticket)
        {
            this.document = document;
            this.ticket = ticket;
            using var image = typeof(TicketPdfRenderer).Assembly.GetManifestResourceStream("Travela.TicketPanorama.png")
                ?? throw new InvalidOperationException("Gömülü bilet panoraması bulunamadı.");
            panorama = XImage.FromStream(image);
            NewPage();
        }

        private XFont Font(double size = 10.5, bool bold = false)
        {
            if (!fonts.TryGetValue((size, bold), out var font))
                fonts[(size, bold)] = font = new XFont("Travela", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular,
                    new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset));
            return font;
        }

        private void NewPage()
        {
            gfx?.Dispose();
            page = document.AddPage();
            page.Size = PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            Draw("Travela.", Font(19, true), XColor.FromArgb(29, 88, 230), Margin, 32, Width, 26);
            var caption = "Bilet detayı" + (document.PageCount > 1 ? " · devam" : "");
            Draw(caption, Font(11), Muted, Margin, 38, Width, 16, XStringFormats.TopRight);
            y = 78;
        }

        private void Draw(string text, XFont font, XColor color, double x, double top, double width, double height, XStringFormat? alignment = null)
        {
            if (text.Length > 0)
                gfx.DrawString(text, font, new XSolidBrush(color), new XRect(x, top, width, height), alignment ?? XStringFormats.TopLeft);
        }

        // Break identifiers and unusually long words at text-element boundaries, without inserting
        // hyphens or dropping characters. Ordinary words wrap only at whitespace.
        private List<string> Wrap(string? text, XFont font, double width)
        {
            var result = new List<string>();
            foreach (var paragraph in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = "";
                foreach (Match word in Regex.Matches(paragraph, @"\S+"))
                {
                    var candidate = line.Length == 0 ? word.Value : line + " " + word.Value;
                    if (gfx.MeasureString(candidate, font).Width <= width) { line = candidate; continue; }
                    if (gfx.MeasureString(word.Value, font).Width <= width)
                    {
                        if (line.Length > 0) result.Add(line);
                        line = word.Value;
                        continue;
                    }
                    if (line.Length > 0) line += " ";
                    var elements = StringInfo.GetTextElementEnumerator(word.Value);
                    while (elements.MoveNext())
                    {
                        var element = elements.GetTextElement();
                        if (line.Length > 0 && gfx.MeasureString(line + element, font).Width > width)
                        { result.Add(line); line = ""; }
                        line += element;
                    }
                }
                result.Add(line);
            }
            return result;
        }

        private List<Line> Lines(string? text, double width, double size = 10.5, XColor? color = null, bool bold = false)
        {
            var font = Font(size, bold);
            return Wrap(text, font, width).Select(t => new Line(t, font, color ?? Body, size * 1.42)).ToList();
        }
        private static double Height(IEnumerable<Line> lines) => lines.Sum(l => l.Height);
        private void Gap(List<Line> lines, double height) => lines.Add(new Line("", Font(), Body, height));
        private void Field(List<Line> lines, string label, string value, double width, double size = 11)
        {
            lines.AddRange(Lines(label, width, 9.5, Muted));
            Gap(lines, 3);
            lines.AddRange(Lines(value, width, size));
            Gap(lines, 14);
        }
        private void Ensure(double height) { if (y + height > Bottom) NewPage(); }
        private void Flow(IReadOnlyList<Line> lines, double after = 6)
        {
            // Keep short paragraphs together; long notes continue at the normal readable size.
            Ensure(Math.Min(Height(lines), 100));
            foreach (var line in lines)
            {
                Ensure(line.Height);
                Draw(line.Text, line.Font, line.Color, Margin, y, Width, line.Height);
                y += line.Height;
            }
            y += after;
        }
        private void Section(string title, bool statuses = false)
        {
            Ensure(70);
            y += 6;
            gfx.DrawLine(new XPen(Rule, .6), Margin, y, Margin + Width, y);
            y += 14;
            if (statuses) Statuses();
            Flow(Lines(title, Width, 14.5, Heading), 8);
        }

        public void Render()
        {
            Flow(Lines($"{ticket.DepartureCity} → {ticket.ArrivalCity}", Width, 20, Heading), 5);
            Flow(Lines("PNR  " + ticket.Pnr, Width, 10.5, Muted), 18);
            Card();
            Terminals();
            Section("Bilet ve ödeme bilgileri", statuses: true);
            if (ticket.DiscountAmount > 0)
            {
                Detail("İndirim öncesi", Money(ticket.OriginalAmount));
                Detail("Kampanya indirimi", "-" + Money(ticket.DiscountAmount) + "\n" + ticket.CampaignName +
                    (ticket.CouponCode == null ? "" : " · " + ticket.CouponCode));
            }
            Detail("İade durumu", ticket.RefundStatusLabel);
            if (ticket.RefundStatus != "None") Detail("İade tutarı", Money(ticket.RefundAmount));
            if (Cancelled && ticket.CancelledAt.HasValue) Detail("İptal zamanı", TurkeyTime(ticket.CancelledAt.Value));
            if (!Cancelled) Detail("İptal son zamanı", TurkeyTime(ticket.CancelDeadline));
            var conditions = Cancelled ? "Bu bilet iptal edilmiştir; seyahat için kullanılamaz." :
                ticket.ChangeCancellationUntil.HasValue ? $"Sefer değişikliği nedeniyle {TurkeyTime(ticket.ChangeCancellationUntil.Value)} tarihine kadar tam iade hakkı tanımlanmıştır." :
                ticket.CanCancel ? "Kalkışa en az 2 saat kala tam iade talep edilebilir; tam 2 saatte iptal kabul edilir." :
                "İptal son zamanı geçtiğinden yolcu iptali ve iade yapılamaz.";
            var notes = new List<List<Line>> { Lines("Tüm saatler Türkiye saatidir (Europe/Istanbul). " + conditions, Width, 10) };
            if (!string.IsNullOrWhiteSpace(ticket.CancellationReason))
                notes.Add(Lines("İptal nedeni (geçmiş kayıt): " + ticket.CancellationReason, Width, 10));
            notes.Add(Lines("Güncel iade açıklaması: " + ticket.RefundDescription, Width, 10));
            notes.Add(Lines("Bu belge üretildiği andaki durumu gösterir. Güncel durum için Biletlerim sayfasını kontrol ediniz.", Width, 9.5));
            // Move a short conditions section as a unit, instead of leaving a few lines on a new page.
            Ensure(Math.Min(notes.Sum(n => Height(n) + 6) + 49, 200));
            Section("İade ve iptal koşulları");
            foreach (var note in notes) Flow(note);
            Footers();
        }

        private string Money(decimal amount) => amount.ToString("N2", Turkish) + " " + ticket.Currency;

        private void Card()
        {
            var widths = new[] { Width * .42, Width * .33, Width * .25 };
            var left = Lines(ticket.CompanyName, widths[0] - 2 * Padding, 14.5, Heading);
            Gap(left, 3);
            left.AddRange(Lines($"{ticket.DepartureCity} → {ticket.ArrivalCity}", widths[0] - 2 * Padding, 11));
            Gap(left, 3);
            left.AddRange(Lines(TurkeyTime(ticket.DepartureTime), widths[0] - 2 * Padding, 10));
            var middle = new List<Line>();
            Field(middle, "Yolcu", ticket.PassengerName, widths[1] - 2 * Padding, 12);
            Field(middle, "Kalkış", TurkeyTime(ticket.DepartureTime), widths[1] - 2 * Padding);
            Field(middle, "Varış", TurkeyTime(ticket.ArrivalTime), widths[1] - 2 * Padding);
            var right = new List<Line>();
            Field(right, "Koltuk", ticket.SeatNumber.ToString(Turkish), widths[2] - 2 * Padding, 29);
            Field(right, "Ödenen tutar", Money(ticket.Amount), widths[2] - 2 * Padding, 13);
            var columns = new[] { left, middle, right };
            var positions = new int[3];
            var imageWidth = widths[0] - 20;
            var imageHeight = imageWidth * panorama.PixelHeight / panorama.PixelWidth;
            var imagePending = true;
            while (imagePending || columns.Where((c, i) => positions[i] < c.Count).Any())
            {
                var textHeights = columns.Select((c, i) => Height(c.Skip(positions[i]))).ToArray();
                var required = Math.Max(198, Math.Max(textHeights[0] + (imagePending ? 16 + imageHeight : 0), Math.Max(textHeights[1], textHeights[2])) + 2 * Padding);
                Ensure(Math.Min(required, 198));
                var height = Math.Min(required, Bottom - y);
                var rect = new XRect(Margin, y, Width, height);
                var state = gfx.Save();
                var shape = new XGraphicsPath();
                shape.AddRoundedRectangle(rect.X, rect.Y, rect.Width, rect.Height, 8, 8);
                gfx.IntersectClip(shape);
                gfx.DrawRectangle(new XSolidBrush(PaleBlue), Margin + widths[0] + widths[1], y, widths[2], height);
                gfx.Restore(state);
                gfx.DrawRoundedRectangle(new XPen(Rule, .75), rect, new XSize(8, 8));
                gfx.DrawLine(new XPen(XColor.FromArgb(165, 48, 78), 1.5), Margin + 9, y + .5, Margin + 89, y + .5);
                gfx.DrawLine(new XPen(Rule, .45), Margin + widths[0], y + 10, Margin + widths[0], y + height - 10);
                var perforation = new XPen(XColor.FromArgb(192, 207, 221), .65) { DashStyle = XDashStyle.Dash };
                gfx.DrawLine(perforation, Margin + widths[0] + widths[1], y + 1, Margin + widths[0] + widths[1], y + height - 1);
                var drawImage = imagePending && textHeights[0] + 16 + imageHeight <= height - 2 * Padding;
                var x = Margin;
                for (var i = 0; i < columns.Length; i++)
                {
                    var top = y + Padding;
                    var end = y + height - Padding - (i == 0 && drawImage ? imageHeight + 16 : 0);
                    while (positions[i] < columns[i].Count)
                    {
                        var line = columns[i][positions[i]];
                        if (top + line.Height > end + .01) break;
                        Draw(line.Text, line.Font, line.Color, x + Padding, top, widths[i] - 2 * Padding, line.Height);
                        top += line.Height;
                        positions[i]++;
                    }
                    x += widths[i];
                }
                if (drawImage)
                {
                    gfx.DrawImage(panorama, Margin + 10, y + height - Padding - imageHeight, imageWidth, imageHeight);
                    imagePending = false;
                }
                y += height + 16;
                if (imagePending || columns.Where((c, i) => positions[i] < c.Count).Any()) NewPage();
            }
        }

        private void Terminals()
        {
            var width = (Width - 28) / 2;
            List<Line> Terminal(string label, string? name, string? address)
            {
                var lines = Lines(label, width, 9.5, Muted);
                Gap(lines, 4);
                lines.AddRange(Lines(name, width, 11, Heading));
                if (!string.IsNullOrWhiteSpace(address)) { Gap(lines, 4); lines.AddRange(Lines(address, width)); }
                return lines;
            }
            var left = string.IsNullOrWhiteSpace(ticket.DepartureTerminalName) ? new List<Line>() : Terminal("Kalkış terminali", ticket.DepartureTerminalName, ticket.DepartureTerminalAddress);
            var right = string.IsNullOrWhiteSpace(ticket.ArrivalTerminalName) ? new List<Line>() : Terminal("Varış terminali", ticket.ArrivalTerminalName, ticket.ArrivalTerminalAddress);
            var positions = new int[2];
            var columns = new[] { left, right };
            while (columns.Where((c, i) => positions[i] < c.Count).Any())
            {
                Ensure(Math.Min(70, columns.Max(c => Height(c))));
                var end = y;
                for (var i = 0; i < 2; i++)
                {
                    var top = y;
                    while (positions[i] < columns[i].Count)
                    {
                        var line = columns[i][positions[i]];
                        if (top + line.Height > Bottom) break;
                        Draw(line.Text, line.Font, line.Color, Margin + i * (width + 28), top, width, line.Height);
                        top += line.Height;
                        positions[i]++;
                    }
                    end = Math.Max(end, top);
                }
                y = end + 12;
                if (columns.Where((c, i) => positions[i] < c.Count).Any()) NewPage();
            }
            if (!string.IsNullOrWhiteSpace(ticket.BoardingInstructions))
                Flow(Lines("Biniş açıklaması: " + ticket.BoardingInstructions, Width), 10);
        }

        private void Statuses()
        {
            var status = Cancelled ? "İPTAL EDİLDİ" : "Aktif";
            var chipWidth = gfx.MeasureString(status, Font(9.5)).Width + gfx.MeasureString("Ödeme doğrulandı", Font(9.5)).Width + 46;
            double x = Margin + Width - chipWidth;
            void Chip(string text, XColor background, XColor foreground)
            {
                var font = Font(9.5);
                var width = gfx.MeasureString(text, font).Width + 18;
                gfx.DrawRoundedRectangle(new XSolidBrush(background), new XRect(x, y, width, 22), new XSize(6, 6));
                Draw(text, font, foreground, x + 9, y + 4, width - 18, 15);
                x += width + 10;
            }
            Chip(status, Cancelled ? XColor.FromArgb(253, 238, 240) : XColor.FromArgb(237, 247, 238), Cancelled ? XColor.FromArgb(153, 57, 70) : XColor.FromArgb(63, 115, 73));
            Chip("Ödeme doğrulandı", XColor.FromArgb(236, 244, 253), Heading);
        }

        private void Detail(string label, string value)
        {
            var labelWidth = 134d;
            var labels = Lines(label + ":", labelWidth - 12, 10, Muted);
            var values = Lines(value, Width - labelWidth, 10.5);
            // Keep short rows together; paginate very long campaign descriptions without clipping.
            Ensure(Math.Min(Math.Max(Height(labels), Height(values)) + 8, 50));
            var count = Math.Max(labels.Count, values.Count);
            for (var i = 0; i < count; i++)
            {
                var height = Math.Max(i < labels.Count ? labels[i].Height : 0, i < values.Count ? values[i].Height : 0);
                Ensure(height);
                if (i < labels.Count) Draw(labels[i].Text, labels[i].Font, labels[i].Color, Margin, y, labelWidth - 12, height);
                if (i < values.Count) Draw(values[i].Text, values[i].Font, values[i].Color, Margin + labelWidth, y, Width - labelWidth, height);
                y += height;
            }
            y += 5;
        }

        private void Footers()
        {
            gfx.Dispose();
            gfx = null!;
            for (var i = 0; i < document.PageCount; i++)
            {
                var p = document.Pages[i];
                using var footer = XGraphics.FromPdfPage(p, XGraphicsPdfPageOptions.Append);
                var top = p.Height.Point - 47;
                footer.DrawLine(new XPen(Rule, .6), Margin, top, p.Width.Point - Margin, top);
                footer.DrawString("Durum bilgisi: " + TurkeyTime(ticket.RetrievedAt) + " · Türkiye (Europe/Istanbul)", Font(8), new XSolidBrush(Muted),
                    new XRect(Margin, top + 12, Width - 40, 14), XStringFormats.TopLeft);
                footer.DrawString($"{i + 1} / {document.PageCount}", Font(8.5), new XSolidBrush(Muted),
                    new XRect(Margin, top + 12, Width, 14), XStringFormats.TopRight);
            }
        }

        public void Dispose() { gfx?.Dispose(); panorama.Dispose(); }
    }
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
