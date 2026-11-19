using System.Globalization;
using BoAdmin.Application.Reports;
using BoAdmin.Application.Reports.Dtos;
using ClosedXML.Excel;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Toplu ad güncelleme Excel dosyasını okur (.xlsx / .xlsm, ilk sayfa).
/// Sütunlar başlıktan bulunur: "CUID" içeren sütun ve "Yeni" / "Ad" / "Name" içeren sütun.
/// Başlık yoksa A = CUID, B = yeni ad kabul edilir.
/// </summary>
public static class RenameFileReader
{
    public const int MaxRows = 5000;

    public sealed record ReadResult(IReadOnlyList<RenameRow> Rows, IReadOnlyList<RenameRowIssue> Issues);

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static ReadResult Read(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();

        var rows = new List<RenameRow>();
        var issues = new List<RenameRowIssue>();
        if (used is null) return new ReadResult(rows, issues);

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();

        // ---- Başlık satırı ----
        int? cuidCol = null, nameCol = null, fallbackNameCol = null;
        for (var c = 1; c <= lastCol; c++)
        {
            var header = Normalize(sheet.Cell(firstRow, c).GetFormattedString());
            if (header.Length == 0) continue;

            if (cuidCol is null && header.Contains("cuid")) cuidCol = c;
            else if (nameCol is null && header.Contains("yeni")) nameCol = c;
            else if (fallbackNameCol is null && (header.Contains("ad") || header.Contains("name") || header.Contains("isim"))) fallbackNameCol = c;
        }

        var hasHeader = cuidCol is not null;
        cuidCol ??= 1;
        nameCol ??= fallbackNameCol ?? (cuidCol == 1 ? 2 : 1);
        var startRow = hasHeader ? firstRow + 1 : firstRow;

        // ---- Satırlar ----
        for (var r = startRow; r <= lastRow; r++)
        {
            var cuid = sheet.Cell(r, cuidCol.Value).GetFormattedString().Trim();
            var newName = sheet.Cell(r, nameCol.Value).GetFormattedString();

            if (cuid.Length == 0 && string.IsNullOrWhiteSpace(newName)) continue; // boş satır

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

            rows.Add(new RenameRow(r, cuid, newName));
        }

        return new ReadResult(rows, issues);
    }

    /// <summary>Kullanıcıya verilecek boş şablon.</summary>
    public static byte[] CreateTemplate()
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add("Ad Güncelleme");
        sheet.Cell(1, 1).Value = "CUID";
        sheet.Cell(1, 2).Value = "Yeni Ad";
        var header = sheet.Range(1, 1, 1, 2);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");
        sheet.Column(1).Width = 32;
        sheet.Column(2).Width = 60;
        sheet.Column(1).Style.NumberFormat.Format = "@"; // metin
        sheet.Column(2).Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(1);

        var help = workbook.Worksheets.Add("Açıklama");
        string[] lines =
        {
            "Rapor adı güncelleme şablonu",
            "",
            "1. 'Ad Güncelleme' sayfasında A sütununa raporun CUID'ini, B sütununa yeni adını yazın.",
            "2. İlk satırdaki başlıkları (CUID, Yeni Ad) silmeyin.",
            "3. Her satır bir rapor. Aynı CUID birden fazla yazılırsa yalnızca ilk satır işlenir.",
            $"4. Tek seferde en fazla {ReportService.MaxBulkSize} rapor işlenir.",
            "5. Yeni ad en fazla 255 karakter olabilir; yeni adı mevcut adla aynı olan satırlar atlanır.",
            "6. Dosyayı yükledikten sonra önizlemede tüm değişiklikleri görüp onaylamadan hiçbir rapor değiştirilmez.",
        };
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Column(1).Width = 110;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Normalize(string value) =>
        value.Trim().ToLower(Tr).Replace('ı', 'i').Replace('ö', 'o').Replace('ü', 'u');
}
