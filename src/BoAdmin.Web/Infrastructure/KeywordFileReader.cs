using System.Globalization;
using BoAdmin.Application.Reports;
using BoAdmin.Application.Reports.Dtos;
using ClosedXML.Excel;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Toplu keyword güncelleme Excel dosyasını okur (.xlsx / .xlsm, ilk sayfa).
/// Sütunlar başlıktan bulunur: "CUID", "Keyword" (veya "Anahtar"), "İşlem" (Ekle / Değiştir).
/// Başlık yoksa A = CUID, B = keyword'ler, C = işlem. İşlem boşsa "Ekle" kabul edilir.
/// </summary>
public static class KeywordFileReader
{
    public const int MaxRows = 5000;

    public sealed record ReadResult(IReadOnlyList<KeywordRow> Rows, IReadOnlyList<RenameRowIssue> Issues);

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static ReadResult Read(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();

        var rows = new List<KeywordRow>();
        var issues = new List<RenameRowIssue>();
        if (used is null) return new ReadResult(rows, issues);

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();

        // ---- Başlık satırı ----
        int? cuidCol = null, keywordCol = null, modeCol = null;
        for (var c = 1; c <= lastCol; c++)
        {
            var header = Normalize(sheet.Cell(firstRow, c).GetFormattedString());
            if (header.Length == 0) continue;

            if (cuidCol is null && header.Contains("cuid")) cuidCol = c;
            else if (keywordCol is null && (header.Contains("keyword") || header.Contains("anahtar") || header.Contains("etiket"))) keywordCol = c;
            else if (modeCol is null && (header.Contains("islem") || header.Contains("mode") || header.Contains("tur"))) modeCol = c;
        }

        var hasHeader = cuidCol is not null;
        cuidCol ??= 1;
        keywordCol ??= 2;
        if (!hasHeader) modeCol ??= 3;
        var startRow = hasHeader ? firstRow + 1 : firstRow;

        // ---- Satırlar ----
        for (var r = startRow; r <= lastRow; r++)
        {
            var cuid = sheet.Cell(r, cuidCol.Value).GetFormattedString().Trim();
            var keywords = sheet.Cell(r, keywordCol.Value).GetFormattedString();
            var modeText = modeCol is null ? string.Empty : sheet.Cell(r, modeCol.Value).GetFormattedString();

            if (cuid.Length == 0 && string.IsNullOrWhiteSpace(keywords) && string.IsNullOrWhiteSpace(modeText)) continue;

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

            var mode = KeywordList.ParseMode(modeText);
            if (mode is null)
            {
                issues.Add(new RenameRowIssue(r, cuid, $"İşlem değeri anlaşılamadı: '{modeText.Trim()}'. 'Ekle' veya 'Değiştir' yazın."));
                continue;
            }

            rows.Add(new KeywordRow(r, cuid, keywords, mode.Value));
        }

        return new ReadResult(rows, issues);
    }

    /// <summary>Kullanıcıya verilecek boş şablon.</summary>
    public static byte[] CreateTemplate()
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add("Keyword Güncelleme");
        sheet.Cell(1, 1).Value = "CUID";
        sheet.Cell(1, 2).Value = "Keyword";
        sheet.Cell(1, 3).Value = "İşlem";

        var header = sheet.Range(1, 1, 1, 3);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");

        sheet.Column(1).Width = 32;
        sheet.Column(2).Width = 60;
        sheet.Column(3).Width = 14;
        sheet.Column(1).Style.NumberFormat.Format = "@";
        sheet.Column(2).Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(1);

        // İşlem sütunu için açılır liste
        sheet.Range(2, 3, 1001, 3).CreateDataValidation().List("\"Ekle,Değiştir\"", true);

        var help = workbook.Worksheets.Add("Açıklama");
        string[] lines =
        {
            "Rapor keyword güncelleme şablonu",
            "",
            "1. 'Keyword Güncelleme' sayfasında A sütununa raporun CUID'ini, B sütununa keyword'leri yazın.",
            "2. Birden fazla keyword'ü virgülle ayırın: Finans, Aylık, Yatırım",
            "3. C sütunu (İşlem):",
            "     Ekle     -> keyword'ler raporun mevcut keyword'lerine eklenir (boş bırakılırsa varsayılan budur)",
            "     Değiştir -> raporun keyword'leri tamamen B sütunundakilerle değiştirilir (B boşsa tüm keyword'ler silinir)",
            "4. Aynı keyword bir kez tutulur (büyük/küçük harf farkı gözetilmez).",
            "5. İlk satırdaki başlıkları silmeyin. Aynı CUID birden fazla yazılırsa yalnızca ilk satır işlenir.",
            $"6. Tek seferde en fazla {ReportService.MaxBulkSize} rapor işlenir.",
            "7. Önizlemede mevcut ve yeni keyword'leri görüp onaylamadan hiçbir rapor değiştirilmez.",
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
