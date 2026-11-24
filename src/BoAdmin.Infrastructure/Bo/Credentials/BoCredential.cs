namespace BoAdmin.Infrastructure.Bo.Credentials;

/// <summary>BO servis kullanıcısı. ToString şifreyi asla göstermez.</summary>
public sealed class BoCredential
{
    public BoCredential(string userName, string password, string source)
    {
        UserName = userName;
        Password = password;
        Source = source;
    }

    public string UserName { get; }
    public string Password { get; }

    /// <summary>"Sbox" veya "Config"</summary>
    public string Source { get; }

    public bool FromSbox => Source == "Sbox";

    public override string ToString() => $"kullanici={UserName}, sifre=****, kaynak={Source}";
}
