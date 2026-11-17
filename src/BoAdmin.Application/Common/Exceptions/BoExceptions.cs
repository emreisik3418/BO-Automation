namespace BoAdmin.Application.Common.Exceptions;

/// <summary>
/// SAP BO kaynaklı hataların temel sınıfı.
/// UserMessage: ekranda gösterilecek Türkçe mesaj. Message: loglar için teknik detay.
/// </summary>
public abstract class BoException : Exception
{
    protected BoException(string userMessage, string technicalMessage, Exception? inner = null,
        string? boErrorCode = null, int? httpStatus = null)
        : base(technicalMessage, inner)
    {
        UserMessage = userMessage;
        BoErrorCode = boErrorCode;
        HttpStatus = httpStatus;
    }

    public string UserMessage { get; }
    public string? BoErrorCode { get; }
    public int? HttpStatus { get; }
}

/// <summary>BO sunucusuna ulaşılamıyor (ağ hatası, zaman aşımı, sunucu kapalı).</summary>
public sealed class BoUnavailableException : BoException
{
    public BoUnavailableException(string technicalMessage, Exception? inner = null)
        : base("SAP BO sunucusuna ulaşılamıyor. Lütfen daha sonra tekrar deneyin veya sistem yöneticisine başvurun.",
            technicalMessage, inner) { }
}

/// <summary>Servis hesabıyla BO oturumu açılamadı veya oturum yenilenemedi.</summary>
public sealed class BoAuthenticationException : BoException
{
    public BoAuthenticationException(string technicalMessage, string? boErrorCode = null, int? httpStatus = null, Exception? inner = null)
        : base("SAP BO oturumu açılamadı. Servis hesabı bilgileri hatalı olabilir veya oturum süresi dolmuş olabilir.",
            technicalMessage, inner, boErrorCode, httpStatus) { }
}

/// <summary>BO servis hesabının bu işlem için yetkisi yok (HTTP 403).</summary>
public sealed class BoAccessDeniedException : BoException
{
    public BoAccessDeniedException(string technicalMessage, string? boErrorCode = null)
        : base("BO servis hesabının bu işlem için yetkisi yok.", technicalMessage, null, boErrorCode, 403) { }
}

/// <summary>Nesne BO'da bulunamadı (HTTP 404).</summary>
public sealed class BoNotFoundException : BoException
{
    public BoNotFoundException(string technicalMessage, string? boErrorCode = null)
        : base("İstenen nesne SAP BO'da bulunamadı. Silinmiş veya taşınmış olabilir.", technicalMessage, null, boErrorCode, 404) { }
}

/// <summary>BO'nun döndürdüğü diğer hatalar.</summary>
public sealed class BoOperationException : BoException
{
    public BoOperationException(string boMessage, string? boErrorCode = null, int? httpStatus = null, Exception? inner = null)
        : base($"SAP BO işlemi başarısız: {boMessage}", boMessage, inner, boErrorCode, httpStatus) { }
}
