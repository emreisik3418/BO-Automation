namespace BoAdmin.Infrastructure.Bo.Credentials;

/// <summary>Sbox (HsmService) çağrısı: kayıttaki "kullanici;sifre" değerini döndürür.</summary>
public interface ISboxClient
{
    Task<BoCredential> GetAsync(SboxOptions service, SboxTargetOptions target, CancellationToken ct = default);
}
