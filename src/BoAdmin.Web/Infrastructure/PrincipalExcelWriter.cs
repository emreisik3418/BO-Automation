using BoAdmin.Application.Principals;
using ClosedXML.Excel;

namespace BoAdmin.Web.Infrastructure;

/// <summary>Kullanıcı listesi ve grup üyelik raporu için Excel dosyaları.</summary>
public static class PrincipalExcelWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Users(IReadOnlyList<UserListItemDto> users, string environment)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Kullanıcılar");

        string[] headers = { "Hesap", "Tam ad", "E-posta", "Durum", "Son giriş", "Oluşturulma", "Açıklama", "ID", "CUID" };
        WriteHeader(ws, headers);

        var r = 2;
        foreach (var u in users)
        {
            ws.Cell(r, 1).Value = u.Name;
            ws.Cell(r, 2).Value = u.FullName ?? "";
            ws.Cell(r, 3).Value = u.Email ?? "";
            ws.Cell(r, 4).Value = u.Disabled ? "Pasif" : "Aktif";
            SetDate(ws.Cell(r, 5), u.LastLogon);
            SetDate(ws.Cell(r, 6), u.CreatedAt);
            ws.Cell(r, 7).Value = u.Description ?? "";
            ws.Cell(r, 8).Value = u.Id;
            ws.Cell(r, 9).Value = u.Cuid ?? "";
            r++;
        }

        Finish(ws, headers.Length, r - 1, environment);
        return Save(wb);
    }

    public static byte[] Membership(MembershipReport report, string environment)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Üyelikler");

        string[] headers = { "Grup", "Alt grup yolu", "Üye türü", "Üye adı", "Tam ad", "E-posta", "Durum", "Son giriş" };
        WriteHeader(ws, headers);

        var r = 2;
        foreach (var row in report.Rows)
        {
            ws.Cell(r, 1).Value = row.RootGroup;
            ws.Cell(r, 2).Value = row.GroupPath;
            ws.Cell(r, 3).Value = row.MemberType;
            ws.Cell(r, 4).Value = row.MemberName;
            ws.Cell(r, 5).Value = row.FullName ?? "";
            ws.Cell(r, 6).Value = row.Email ?? "";
            ws.Cell(r, 7).Value = row.Disabled is null ? "" : row.Disabled.Value ? "Pasif" : "Aktif";
            SetDate(ws.Cell(r, 8), row.LastLogon);
            r++;
        }

        Finish(ws, headers.Length, r - 1, environment);

        if (report.Warnings.Count > 0)
        {
            var info = wb.Worksheets.Add("Uyarılar");
            for (var i = 0; i < report.Warnings.Count; i++) info.Cell(i + 1, 1).Value = report.Warnings[i];
            info.Column(1).Width = 120;
        }

        return Save(wb);
    }

    private static void WriteHeader(IXLWorksheet ws, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        var h = ws.Range(1, 1, 1, headers.Length);
        h.Style.Font.Bold = true;
        h.Style.Font.FontColor = XLColor.White;
        h.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");
    }

    private static void SetDate(IXLCell cell, DateTime? value)
    {
        if (value is null) return;
        cell.Value = value.Value;
        cell.Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
    }

    private static void Finish(IXLWorksheet ws, int columns, int lastRow, string environment)
    {
        if (lastRow >= 2) ws.Range(1, 1, lastRow, columns).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, columns).AdjustToContents(1, Math.Min(lastRow, 500));
        foreach (var col in ws.Columns(1, columns))
            if (col.Width > 60) col.Width = 60;

        ws.Cell(lastRow + 2, 1).Value = $"Ortam: {environment} · Oluşturulma: {DateTime.Now:dd.MM.yyyy HH:mm}";
        ws.Cell(lastRow + 2, 1).Style.Font.Italic = true;
        ws.Cell(lastRow + 2, 1).Style.Font.FontColor = XLColor.Gray;
    }

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
