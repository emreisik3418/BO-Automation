using BoAdmin.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
#if SBOX_PROXY
using System.ServiceModel;
using BoAdmin.Infrastructure.Sbox;
#endif

namespace BoAdmin.Infrastructure.Bo.Credentials;

/// <summary>
/// Sbox HsmService istemcisi (universe-object-mapping / unv-csharp'taki SboxCredentialProvider'ın .NET 8 hâli).
///
/// WCF proxy sınıfları (HsmServiceClient, OpSysSensDataNameRequest ...) WSDL'den üretilir ve repoya konmaz:
///   tools\generate-sbox-proxy.bat "http://SBOX-SUNUCU/HsmService.svc?wsdl"
/// Üretilen dosya (Sbox\HsmServiceProxy.cs) varsa proje SBOX_PROXY sembolüyle derlenir.
/// Yoksa proje yine derlenir; Sbox kullanan bir ortama bağlanılırken anlaşılır bir hata verilir.
/// </summary>
public sealed class SboxClient : ISboxClient
{
    private readonly ILogger<SboxClient> _logger;

    public SboxClient(ILogger<SboxClient> logger) => _logger = logger;

#if SBOX_PROXY
    public async Task<BoCredential> GetAsync(SboxOptions service, SboxTargetOptions target, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(service.Endpoint))
            throw new BoAuthenticationException("Sbox adresi tanımlı değil (SapBo:Sbox:Endpoint).");
        if (string.IsNullOrWhiteSpace(service.UserName) || string.IsNullOrWhiteSpace(service.Password))
            throw new BoAuthenticationException("Sbox kullanıcı adı veya şifresi tanımlı değil (SapBo:Sbox:UserName / Password).");

        var timeout = TimeSpan.FromSeconds(Math.Max(5, service.TimeoutSeconds));
        var binding = new BasicHttpBinding(service.Endpoint.StartsWith("https", StringComparison.OrdinalIgnoreCase)
            ? BasicHttpSecurityMode.Transport
            : BasicHttpSecurityMode.None)
        {
            OpenTimeout = timeout,
            SendTimeout = timeout,
            ReceiveTimeout = timeout,
            CloseTimeout = timeout,
            MaxReceivedMessageSize = 10 * 1024 * 1024
        };

        var client = new HsmServiceClient(binding, new EndpointAddress(service.Endpoint));
        OpSysSensDataListDVO response;
        try
        {
            var request = new OpSysSensDataNameRequest
            {
                AppName = target.AppName,
                SubAppName = target.SubAppName,
                SensitiveDataName = target.SensitiveDataName,
                OperationRequest = new OpRequestDVO
                {
                    ClientIP = target.Ip,
                    ServerIP = target.Ip,
                    KullName = service.UserName,
                    KullPass = service.Password
                }
            };

            response = await client.GetParametersWithNameAsync(request).WaitAsync(ct);
            client.Close();
        }
        catch (Exception ex)
        {
            client.Abort(); // hatalı WCF kanalı Close ile değil Abort ile kapatılır
            _logger.LogError(ex, "Sbox çağrısı başarısız: {Target}", target);
            throw new BoAuthenticationException($"Sbox servisine ulaşılamadı ({target}): {ex.Message}");
        }

        if (response is null)
            throw new BoAuthenticationException($"Sbox boş yanıt döndü ({target}).");

        if (!string.IsNullOrEmpty(response.OperationResult?.UserMsg))
            throw new BoAuthenticationException($"Sbox hata döndü ({target}): {response.OperationResult.UserMsg}");

        if (response.SensDataList is not null)
        {
            foreach (var sd in response.SensDataList)
            {
                if (sd is not null && target.SensitiveDataName == sd.MBL_SENSDATA_NAME)
                    return Parse(sd.MBL_SENSDATA_ENCRYPTED_VALUE, target);
            }
        }

        throw new BoAuthenticationException($"Sbox'ta kayıt bulunamadı ({target}).");
    }
#else
    public Task<BoCredential> GetAsync(SboxOptions service, SboxTargetOptions target, CancellationToken ct = default)
    {
        _logger.LogError("Sbox proxy sınıfları üretilmemiş; {Target} için kimlik bilgisi alınamıyor.", target);
        throw new BoAuthenticationException(
            "Bu ortam şifresini Sbox'tan alacak şekilde ayarlı, ama uygulama Sbox desteği olmadan derlenmiş. " +
            "tools\\generate-sbox-proxy.bat ile proxy'yi üretip yeniden derleyin.");
    }
#endif

    /// <summary>Beklenen biçim "kullanici;sifre". Şifrede ';' varsa korunur.</summary>
    private static BoCredential Parse(string? value, SboxTargetOptions target)
    {
        var idx = value?.IndexOf(';') ?? -1;
        if (value is null || idx <= 0 || idx == value.Length - 1)
            throw new BoAuthenticationException($"Sbox değeri 'kullanici;sifre' biçiminde değil ({target}).");

        return new BoCredential(value[..idx], value[(idx + 1)..], "Sbox");
    }
}
