using System.Globalization;
using BoAdmin.Application.Principals;
using ClosedXML.Excel;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Toplu grup üyeliği Excel'ini okur (ilk sayfa).
///  Kullanıcı: "Grup" | "Hesap"    | "İşlem" (Ekle / Çıkar)
///  Grup     : "Üst Grup" | "Alt Grup" | "İşlem"
/// Başlık yoksa A = grup, B = üye, C = işlem.
/// </summary>
public static class MembershipFileReader
{
    public const int MaxRows = 5000;

    public sealed record ReadResult(IReadOnlyList<MembershipChangeRow> Rows, IReadOnlyList<PrincipalRowIssue> Issues);

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static ReadResult Read(Stream stream, MemberType type)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();

        var rows = new List<MembershipChangeRow>();
        var issues = new List<PrincipalRowIssue>();
        if (used is null) return new ReadResult(rows, issues);

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();

        int? groupCol = null, memberCol = null, actionCol = null;
        for (var c = 1; c <= lastCol; c++)
        {
            var h = Normalize(sheet.Cell(firstRow, c).GetFormattedString());
            if (h.Length == 0) continue;

            if (actionCol is null && (h.Contains("islem") || h.Contains("action")))
                actionCol = c;
            else if (type == MemberType.Group && memberCol is null && (h.Contains("alt") || h.Contains("child") || h.Contains("sub")))
                memberCol = c;
            else if (type == MemberType.User && memberCol is null && (h.Contains("hesap") || h.Contains("kullanici") || h.Contains("user") || h.Contains("account")))
                memberCol = c;
            else if (groupCol is null && (h.Contains("grup") || h.Contains("group")))
                groupCol = c;
        }

        var hasHeader = groupCol is not null || memberCol is not null || actionCol is not null;
        groupCol ??= 1;
        memberCol ??= 2;
        actionCol ??= 3;
        var start = hasHeader ? firstRow + 1 : firstRow;

        for (var r = start; r <= lastRow; r++)
        {
            var group = sheet.Cell(r, groupCol.Value).GetFormattedString().Trim();
            var member = sheet.Cell(r, memberCol.Value).GetFormattedString().Trim();
            var action = sheet.Cell(r, actionCol.Value).GetFormattedString().Trim();
            if (group.Length == 0 && member.Length == 0 && action.Length == 0) continue;

            if (rows.Count + issues.Count >= MaxRows)
            {
                issues.Add(new PrincipalRowIssue(r, null, $"Dosyada en fazla {MaxRows} satır okunur; sonraki satırlar atlandı."));
                break;
            }

            var label = $"{group} / {member}";
            if (group.Length == 0 || member.Length == 0)
            {
                issues.Add(new PrincipalRowIssue(r, label, type == MemberType.User ? "Grup veya hesap boş." : "Üst grup veya alt grup boş."));
                continue;
            }
            if (Invalid(group) || Invalid(member))
            {
                issues.Add(new PrincipalRowIssue(r, label, "Ad geçersiz karakter içeriyor (' \" ; kullanılamaz)."));
                continue;
            }

            rows.Add(new MembershipChangeRow(r, group, member, ParseAction(action)));
        }

        return new ReadResult(rows, issues);
    }

    /// <summary>"Ekle" → true, "Çıkar" → false, tanınmazsa null.</summary>
    public static bool? ParseAction(string value) => Normalize(value) switch
    {
        "ekle" or "add" or "+" => true,
        "cikar" or "sil" or "remove" or "-" => false,
        _ => null
    };

    public static byte[] CreateTemplate(MemberType type)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(type == MemberType.User ? "Kullanıcı Üyelikleri" : "Alt Gruplar");

        ws.Cell(1, 1).Value = type == MemberType.User ? "Grup" : "Üst Grup";
        ws.Cell(1, 2).Value = type == MemberType.User ? "Hesap" : "Alt Grup";
        ws.Cell(1, 3).Value = "İşlem";

        var header = ws.Range(1, 1, 1, 3);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");
        ws.Column(1).Width = 40;
        ws.Column(2).Width = 40;
        ws.Column(3).Width = 12;
        ws.Columns(1, 2).Style.NumberFormat.Format = "@";
        ws.Range("C2:C5001").CreateDataValidation().List("\"Ekle,Çıkar\"", true);
        ws.SheetView.FreezeRows(1);

        var help = wb.Worksheets.Add("Bilgi");
        var lines = type == MemberType.User
            ? new[]
            {
                "Gruba kullanıcı ekleme / çıkarma şablonu",
                "",
                "1. A: grup adı (CMC'deki ad), B: kullanıcının hesap adı, C: Ekle veya Çıkar.",
                "2. Zaten üye olan eklenmez, üye olmayan çıkarılmaz; önizlemede 'atlanacak' olarak görünür.",
                "3. Everyone grubunun üyeliğini BO kendisi yönetir; bu grup için satırlar işlenmez.",
                $"4. Tek seferde en fazla {MembershipChangeService.MaxBatchSize} satır.",
            }
            : new[]
            {
                "Gruba alt grup ekleme / çıkarma şablonu",
                "",
                "1. A: üst grup adı, B: alt grup olarak eklenecek / çıkarılacak grubun adı, C: Ekle veya Çıkar.",
                "2. Zaten alt grup olan eklenmez, olmayan çıkarılmaz.",
                "3. Bir grup kendisine alt grup olarak eklenemez.",
                $"4. Tek seferde en fazla {MembershipChangeService.MaxBatchSize} satır.",
            };
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Column(1).Width = 110;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static bool Invalid(string name) =>
        name.Length > 255 || name.Any(ch => char.IsControl(ch) || ch is '\'' or '"' or ';');

    private static string Normalize(string value) =>
        value.Trim().ToLower(Tr).Replace('ı', 'i').Replace('ş', 's').Replace('ö', 'o').Replace('ü', 'u').Replace('ç', 'c').Replace('ğ', 'g');
}
