using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Travela.DTOs;

namespace Travela.Api.Trips;

public static class TripWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const int MaxTrips = 1_048_575;
    public static string FileName(DateTimeOffset now) => $"seferler_{TripManagementQuery.LocalTime(now.UtcDateTime):yyyy-MM-dd_HH-mm}.xlsx";
    private static readonly string[] Headers = ["Sefer ID", "Firma", "Kalkış şehri", "Varış şehri", "Otobüs plakası", "Kalkış tarihi ve saati", "Varış tarihi ve saati", "Oluşturulma tarihi ve saati", "Bilet fiyatı", "Para birimi", "Toplam koltuk", "Boş koltuk", "Dolu koltuk", "Durum"];

    public static async Task<int> Write(Stream output, IAsyncEnumerable<TripListDto> trips, TripManagementFilters filters, DateTimeOffset now, CancellationToken ct)
    {
        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook, true);
        var book = document.AddWorkbookPart(); book.Workbook = new Workbook();
        book.AddNewPart<WorkbookStylesPart>().Stylesheet = Styles();
        var sheets = book.Workbook.AppendChild(new Sheets());
        var tripSheet = book.AddNewPart<WorksheetPart>(); sheets.Append(new Sheet { Id = book.GetIdOfPart(tripSheet), SheetId = 1, Name = "Seferler" });
        int count = 0;
        using (var writer = OpenXmlWriter.Create(tripSheet)) {
            StartSheet(writer, [12, 28, 24, 24, 20, 24, 24, 27, 17, 15, 17, 15, 15, 20]);
            writer.WriteStartElement(new SheetData());
            WriteRow(writer, Headers.Select(h => TextCell(h, 1)).ToArray());
            await foreach (var trip in trips.WithCancellation(ct)) {
                if (++count > MaxTrips) throw new ArgumentException("Excel sayfa sınırı aşıldı. Lütfen tarih veya firma filtreleriyle kayıt sayısını azaltın.");
                WriteRow(writer, [Number(trip.Id, 4), TextCell(trip.CompanyName), TextCell(trip.DepartureCity), TextCell(trip.ArrivalCity), TextCell(trip.BusPlateNumber),
                    Date(trip.DepartureTime), Date(trip.ArrivalTime), Date(trip.CreatedAt), Number(trip.Price, 3), TextCell("TRY"),
                    Number(trip.BusCapacity, 4), Number(trip.AvailableSeats, 4), Number(trip.BusCapacity - trip.AvailableSeats, 4), TextCell(Status(trip.Status))]);
            }
            writer.WriteEndElement();
            writer.WriteElement(new AutoFilter { Reference = $"A1:N{count + 1}" });
            writer.WriteEndElement();
        }
        ct.ThrowIfCancellationRequested();
        var info = book.AddNewPart<WorksheetPart>(); sheets.Append(new Sheet { Id = book.GetIdOfPart(info), SheetId = 2, Name = "Rapor Bilgisi" });
        using (var writer = OpenXmlWriter.Create(info)) {
            StartSheet(writer, [32, 100]); writer.WriteStartElement(new SheetData());
            WriteRow(writer, [TextCell("Rapor bilgisi", 1), TextCell("Değer", 1)]);
            WriteRow(writer, [TextCell("Aktarım zamanı", 5), Date(now.UtcDateTime)]);
            WriteRow(writer, [TextCell("Saat dilimi", 5), TextCell("Europe/Istanbul")]);
            WriteRow(writer, [TextCell("Toplam kayıt sayısı", 5), Number(count, 4)]);
            WriteRow(writer, [TextCell("Sıralama", 5), TextCell(TripManagementQuery.SortLabel(filters) + "; bilinmeyen oluşturulma tarihleri sonda")]);
            foreach (var (name, value) in FilterInfo(filters, now)) WriteRow(writer, [TextCell(name, 5), TextCell(value)]);
            WriteRow(writer, [TextCell("Koltuk tanımı", 5), TextCell("Toplam: sefer kapasite anlık görüntüsü (yoksa otobüs kapasitesi). Boş: listedeki mevcut envanter. Dolu = toplam − boş; bloke koltukları ve envanterde yer tutan aktif rezervasyonları içerir.")]);
            writer.WriteEndElement(); writer.WriteEndElement();
        }
        book.Workbook.Save(); return count;
    }

    public static IEnumerable<(string Name, string Value)> FilterInfo(TripManagementFilters f, DateTimeOffset now)
    {
        string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "Tümü" : value.Trim();
        yield return ("Oluşturma tarihi", TripManagementQuery.CreationWindow(f, now).Label);
        yield return ("Arama", Text(f.Search)); yield return ("Firma ID", f.CompanyId?.ToString(CultureInfo.InvariantCulture) ?? "Tüm firmalar");
        yield return ("Kalkış şehri", Text(f.DepartureCity)); yield return ("Varış şehri", Text(f.ArrivalCity));
        yield return ("Kalkış başlangıcı", f.DepartureFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Sınırsız");
        yield return ("Kalkış bitişi", f.DepartureTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Sınırsız");
        yield return ("Durum", string.IsNullOrWhiteSpace(f.Status) ? "Tüm durumlar" : Status(f.Status)); yield return ("Plaka", Text(f.BusPlate));
        yield return ("En düşük fiyat (TRY)", f.MinPrice?.ToString(CultureInfo.InvariantCulture) ?? "Sınırsız");
        yield return ("En yüksek fiyat (TRY)", f.MaxPrice?.ToString(CultureInfo.InvariantCulture) ?? "Sınırsız");
    }
    private static string Status(string value) => TripManagementQuery.NormalizeStatus(value) switch { "Scheduled" => "Planlandı", "InProgress" => "Devam Ediyor", "Completed" => "Tamamlandı", "Cancelled" => "İptal Edildi", _ => value };
    private static void StartSheet(OpenXmlWriter writer, double[] widths)
    {
        writer.WriteStartElement(new Worksheet());
        writer.WriteElement(new SheetViews(new SheetView(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }));
        writer.WriteElement(new Columns(widths.Select((w, i) => new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = w, CustomWidth = true })));
    }
    private static void WriteRow(OpenXmlWriter writer, Cell[] cells) {
        var row = new Row(); if (cells[0].StyleIndex?.Value == 1) { row.Height = 32; row.CustomHeight = true; }
        writer.WriteStartElement(row); foreach (var cell in cells) writer.WriteElement(cell); writer.WriteEndElement();
    }
    private static Cell TextCell(string value, uint style = 0)
    {
        // Inline text is never interpreted as a formula, even when it starts with =, +, - or @.
        var safe = new StringBuilder();
        foreach (var rune in value.EnumerateRunes()) {
            var c = rune.Value;
            if ((c is 9 or 10 or 13 || c >= 32 && c <= 0xD7FF || c >= 0xE000 && c <= 0xFFFD || c >= 0x10000 && c <= 0x10FFFF) && safe.Length + rune.Utf16SequenceLength <= 32767) safe.Append(rune);
        }
        return new Cell(new InlineString(new Text(safe.ToString()) { Space = SpaceProcessingModeValues.Preserve })) { DataType = CellValues.InlineString, StyleIndex = style };
    }
    private static Cell Number(decimal value, uint style) => new(new CellValue(value.ToString(CultureInfo.InvariantCulture))) { DataType = CellValues.Number, StyleIndex = style };
    private static Cell Date(DateTime? value) => value.HasValue ? Number((decimal)TripManagementQuery.LocalTime(value.Value).ToOADate(), 2) : new Cell { StyleIndex = 2 };
    private static Stylesheet Styles() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "dd.mm.yyyy hh:mm" }, new NumberingFormat { NumberFormatId = 165, FormatCode = "#,##0.00" }) { Count = 2 },
        new Fonts(new Font { FontSize = new FontSize { Val = 11 }, FontName = new FontName { Val = "Calibri" } }, new Font { Bold = new Bold(), Color = new Color { Rgb = "FFFFFFFF" }, FontSize = new FontSize { Val = 11 } }, new Font { Bold = new Bold() }) { Count = 3 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 }), new Fill(new PatternFill(new ForegroundColor { Rgb = "FF173153" }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid })) { Count = 3 },
        new Borders(new Border()) { Count = 1 }, new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
            new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Center }) { FontId = 1, FillId = 2, BorderId = 0, FormatId = 0, ApplyFill = true, ApplyFont = true, ApplyAlignment = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true }, new CellFormat { NumberFormatId = 165, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 3, ApplyNumberFormat = true }, new CellFormat { FontId = 2, ApplyFont = true }) { Count = 6 },
        new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
}
