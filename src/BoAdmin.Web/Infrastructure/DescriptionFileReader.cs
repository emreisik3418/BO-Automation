using System.Globalization;
using BoAdmin.Application.Reports;
using BoAdmin.Application.Reports.Dtos;
using ClosedXML.Excel;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Toplu açıklama güncelleme Excel dosyasını okur (.xlsx / .xlsm, ilk sayfa).
/// Sütunlar başlıktan bulunur: "CUID" ve "Açıklama" (veya "Description").
/// Başlık yoksa A = CUID, B = yeni açıklama. Hücre içi satır sonları (Alt+Enter) korunur.
/// </summary>
public static class DescriptionFileReader
{
    public const int MaxRows = 5000;

    public sealed record ReadResult(IReadOnlyList<DescriptionRow> Rows, IReadOnlyList<RenameRowIssue> Issues);

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static ReadResult Read(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();

        var rows = new List<DescriptionRow>();
        var issues = new List<RenameRowIssue>();
        if (used is null) return new ReadResult(rows, issues);

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();

        // ---- Başlık satırı ----
        int? cuidCol = null, descriptionCol = null;
        for (var c = 1; c <= lastCol; c++)
        {
            var header = Normalize(sheet.Cell(firstRow, c).GetFormattedString());
            if (header.Length == 0) continue;

            if (cuidCol is null && header.Contains("cuid")) cuidCol = c;
            else if (descriptionCol is null && (header.Contains("aciklama") || header.Contains("description") || header.Contains("tanim")))
                descriptionCol = c;
        }

        var hasHeader = cuidCol is not null;
        cuidCol ??= 1;
        descriptionCol ??= cuidCol == 1 ? 2 : 1;
        var startRow = hasHeader ? firstRow + 1 : firstRow;

        // ---- Satırlar ----
        for (var r = startRow; r <= lastRow; r++)
        {
            var cuid = sheet.Cell(r, cuidCol.Value).GetFormattedString().Trim();
            var description = sheet.Cell(r, descriptionCol.Value).GetFormattedString();

            if (cuid.Length == 0 && string.IsNullOrWhiteSpace(description)) continue;

            if (rows.Count + issues.Count >= MaxRows)
            {
                issues.Add(new RenameRowIssue(r, null, $"Dosyada en fazla {MaxRows} satır okunur; sonraki satırlar atlandı."));
                break;
            }

            if (cuid.Length == 0)
            {
                issues.Add(new RenameRowIssue(r, null, "CUID boş."));
                continue;
            }

            if (!CuidListParser.IsValidCuid(cuid))
            {
                issues.Add(new RenameRowIssue(r, cuid, "CUID biçimi geçersiz."));
                continue;
            }

            rows.Add(new DescriptionRow(r, cuid, description));
        }

        return new ReadResult(rows, issues);
    }

    /// <summary>Kullanıcıya verilecek boş şablon.</summary>
    public static byte[] CreateTemplate()
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add("Açıklama Güncelleme");
        sheet.Cell(1, 1).Value = "CUID";
        sheet.Cell(1, 2).Value = "Yeni Açıklama";

        var header = sheet.Range(1, 1, 1, 2);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");

        sheet.Column(1).Width = 32;
        sheet.Column(2).Width = 90;
        sheet.Column(1).Style.NumberFormat.Format = "@";
        sheet.Column(2).Style.NumberFormat.Format = "@";
        sheet.Column(2).Style.Alignment.WrapText = true;
        sheet.SheetView.FreezeRows(1);

        var help = workbook.Worksheets.Add("Bilgi");
        string[] lines =
        {
            "Rapor açıklaması (description) güncelleme şablonu",
            "",
            "1. 'Açıklama Güncelleme' sayfasında A sütununa raporun CUID'ini, B sütununa yeni açıklamayı yazın.",
            "2. Raporun mevcut açıklaması tamamen B sütunundaki metinle değiştirilir.",
            "3. Hücre içinde alt satıra geçmek için Alt+Enter kullanabilirsiniz; satır sonları korunur.",
            $"4. Açıklama boş olamaz ve en fazla {DescriptionText.MaxLength} karakter olabilir.",
            "5. İlk satırdaki başlıkları silmeyin. Aynı CUID birden fazla yazılırsa yalnızca ilk satır işlenir.",
            $"6. Tek seferde en fazla {ReportService.MaxBulkSize} rapor işlenir.",
            "7. Önizlemede mevcut ve yeni açıklamayı görüp onaylamadan hiçbir rapor değiştirilmez.",
        };
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Column(1).Width = 120;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Normalize(string value) =>
        value.Trim().ToLower(Tr).Replace('ı', 'i').Replace('ş', 's').Replace('ö', 'o').Replace('ü', 'u').Replace('ç', 'c').Replace('ğ', 'g');
}
