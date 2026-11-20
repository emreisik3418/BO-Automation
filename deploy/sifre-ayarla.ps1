<#
  BO servis kullanıcısının şifresini appsettings yerine sunucunun ortam değişkenine yazar.
  Uygulama "SapBo__Environments__{ORTAM}__Password" değişkenini otomatik okur.
  Kullanım (yönetici PowerShell):
      .\sifre-ayarla.ps1 -Ortam TEST
      .\sifre-ayarla.ps1 -Ortam PROD
  Şifre ekranda görünmez ve hiçbir dosyaya yazılmaz. Servis kuruluysa otomatik yeniden başlatılır.
  Önce servis-kur.ps1, sonra bu betik çalıştırılmalı.
#>
param(
    [Parameter(Mandatory = $true)][string]$Ortam,
    [string]$ServiceName = "BoAdmin"
)

$ErrorActionPreference = "Stop"

$secure = Read-Host "$Ortam ortamı BO şifresi" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $name = "SapBo__Environments__$($Ortam.ToUpperInvariant())__Password"
    # 1) Makine ortam değişkeni: exe'yi konsoldan/çift tıkla çalıştırınca okunur
    [Environment]::SetEnvironmentVariable($name, $plain, "Machine")
    Write-Host "Kaydedildi: $name (makine ortam değişkeni)"

    # 2) Servise özel ortam değişkeni: Windows servisleri yeni makine değişkenlerini
    #    sunucu yeniden başlayana kadar görmez; bu kayıt servis her başladığında okunur.
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (Test-Path $key) {
        $current = @((Get-ItemProperty -Path $key -Name Environment -ErrorAction SilentlyContinue).Environment)
        $list = @($current | Where-Object { $_ -and -not $_.StartsWith("$name=") }) + "$name=$plain"
        Set-ItemProperty -Path $key -Name Environment -Value ([string[]]$list) -Type MultiString
        Write-Host "Servise de tanımlandı: $ServiceName"
    } else {
        Write-Host "Servis henüz kurulu değil; servisi kurduktan sonra bu betiği tekrar çalıştırın." -ForegroundColor Yellow
    }
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Restart-Service -Name $ServiceName
    Write-Host "Servis yeniden başlatıldı."
}
