using System.Globalization;
using BoAdmin.Application.Principals;
using ClosedXML.Excel;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Toplu kullanıcı işlemi dosyasını okur.
///  .xlsx: ilk sayfa; "Hesap" sütunu (+ aktif/pasif için "İşlem" sütunu). Başlık yoksa A = hesap, B = işlem.
///  .txt / .csv: her satırda bir hesap (yalnızca silme için).
/// </summary>
public static class UserActionFileReader
{
    public const int MaxRows = 5000;

    public sealed record ReadResult(IReadOnlyList<UserActionRow> Rows, IReadOnlyList<PrincipalRowIssue> Issues);

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static bool IsExcel(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".xlsx" or ".xlsm";

    public static bool IsText(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".txt" or ".csv";

    public static ReadResult ReadText(Stream stream)
    {
        var rows = new List<UserActionRow>();
        var issues = new List<PrincipalRowIssue>();
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var n = 0;
        while (reader.ReadLine() is { } line)
        {
            n++;
            var account = line.Split(';', ',', '\t')[0].Trim().Trim('"');
            if (account.Length == 0) continue;
            if (n == 1 && Normalize(account) is "hesap" or "kullanici" or "account" or "username") continue;

            if (rows.Count >= MaxRows)
            {
                issues.Add(new PrincipalRowIssue(n, null, $"Dosyada en fazla {MaxRows} satır okunur; sonraki satırlar atlandı."));
                break;
            }
            Add(rows, issues, n, account, null);
        }

        return new ReadResult(rows, issues);
    }

    public static ReadResult ReadExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();

        var rows = new List<UserActionRow>();
        var issues = new List<PrincipalRowIssue>();
        if (used is null) return new ReadResult(rows, issues);

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();

        int? accountCol = null, actionCol = null;
        for (var c = 1; c <= lastCol; c++)
        {
            var h = Normalize(sheet.Cell(firstRow, c).GetFormattedString());
            if (h.Length == 0) continue;
            if (accountCol is null && (h.Contains("hesap") || h.Contains("kullanici") || h.Contains("account") || h.Contains("user")))
                accountCol = c;
            else if (actionCol is null && (h.Contains("islem") || h.Contains("durum") || h.Contains("action") || h.Contains("status")))
                actionCol = c;
        }

        var hasHeader = accountCol is not null;
        accountCol ??= 1;
        actionCol ??= accountCol == 1 ? 2 : 1;
        var start = hasHeader ? firstRow + 1 : firstRow;

        for (var r = start; r <= lastRow; r++)
        {
            var account = sheet.Cell(r, accountCol.Value).GetFormattedString().Trim();
            var action = sheet.Cell(r, actionCol.Value).GetFormattedString().Trim();
            if (account.Length == 0 && action.Length == 0) continue;

            if (rows.Count + issues.Count >= MaxRows)
            {
                issues.Add(new PrincipalRowIssue(r, null, $"Dosyada en fazla {MaxRows} satır okunur; sonraki satırlar atlandı."));
                break;
            }
            Add(rows, issues, r, account, ParseAction(action));
        }

        return new ReadResult(rows, issues);
    }

    /// <summary>"Pasif" → true (disable), "Aktif" → false, tanınmazsa null.</summary>
    public static bool? ParseAction(string value) => Normalize(value) switch
    {
        "pasif" or "disable" or "disabled" or "kapat" or "0" => true,
        "aktif" or "enable" or "enabled" or "ac" or "1" => false,
        _ => null
    };

    private static void Add(List<UserActionRow> rows, List<PrincipalRowIssue> issues, int row, string account, bool? disable)
    {
        if (account.Length == 0)
        {
            issues.Add(new PrincipalRowIssue(row, null, "Hesap boş."));
            return;
        }
        if (account.Length > 255 || account.Any(ch => char.IsControl(ch) || ch is '\'' or '"' or ';'))
        {
            issues.Add(new PrincipalRowIssue(row, account, "Hesap adı geçersiz karakter içeriyor."));
            return;
        }
        rows.Add(new UserActionRow(row, account, disable));
    }

    public static byte[] CreateTemplate(UserActionKind kind)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(kind == UserActionKind.Delete ? "Silinecek Kullanıcılar" : "Aktif Pasif");

        ws.Cell(1, 1).Value = "Hesap";
        if (kind == UserActionKind.SetStatus) ws.Cell(1, 2).Value = "İşlem";

        var cols = kind == UserActionKind.SetStatus ? 2 : 1;
        var header = ws.Range(1, 1, 1, cols);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");
        ws.Column(1).Width = 36;
        ws.Column(1).Style.NumberFormat.Format = "@";
        ws.SheetView.FreezeRows(1);

        if (kind == UserActionKind.SetStatus)
        {
            ws.Column(2).Width = 14;
            ws.Range("B2:B5001").CreateDataValidation().List("\"Aktif,Pasif\"", true);
        }

        var help = wb.Worksheets.Add("Bilgi");
        var lines = kind == UserActionKind.Delete
            ? new[]
            {
                "Toplu kullanıcı silme şablonu",
                "",
                "1. A sütununa silinecek kullanıcıların BO hesap adını yazın (CMC'deki 'Account Name').",
                $"2. Tek seferde en fazla {UserActionService.MaxBatchSize} kullanıcı.",
                "3. Administrator, Guest gibi sistem hesapları ve uygulamanın servis hesabı silinmez.",
                "4. Önizlemede listeyi görüp onaylamadan hiçbir kullanıcı silinmez. Silme geri alınamaz.",
            }
            : new[]
            {
                "Toplu aktif / pasif yapma şablonu",
                "",
                "1. A sütununa BO hesap adını, B sütununa 'Aktif' veya 'Pasif' yazın (açılır listeden seçebilirsiniz).",
                "2. Zaten istenen durumda olan kullanıcılar atlanır.",
                $"3. Tek seferde en fazla {UserActionService.MaxBatchSize} kullanıcı.",
                "4. Administrator, Guest gibi sistem hesapları ve uygulamanın servis hesabı değiştirilmez.",
            };
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Column(1).Width = 110;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Normalize(string value) =>
        value.Trim().ToLower(Tr).Replace('ı', 'i').Replace('ş', 's').Replace('ö', 'o').Replace('ü', 'u').Replace('ç', 'c').Replace('ğ', 'g');
}
