# BO-Automation (BoAdmin)

SAP BusinessObjects 4.3 yönetim web uygulaması.
ASP.NET Core MVC (.NET 8), Windows Authentication, BI Platform RESTful Web Services (`/biprws`).

## Proje yapısı

```
src/
├─ BoAdmin.Web/             # MVC: Controller, View, yetkilendirme, layout
├─ BoAdmin.Application/     # Servisler, DTO'lar, arayüzler (BO'dan bağımsız)
└─ BoAdmin.Infrastructure/  # BO REST client, oturum yönetimi, audit log (dosya)
```

Bağımlılık yönü: `Web → Application ← Infrastructure` (Web, composition root olarak Infrastructure'ı da referanslar).

## Modüller

| Modül | Durum |
|---|---|
| Rapor Yönetişimi (`Reports`) | Geliştiriliyor |
| Kullanıcı Yönetimi (`Users`) | Yakında |
| Grup Yönetimi (`Groups`) | Yakında |

## Giriş ekranı

Kimlik doğrulama Windows Authentication ile otomatik yapılır; şifre sorulmaz. Tarayıcı oturumunun ilk
açılışında `/Account/Login` ekranı kullanıcının adını, sicilini ve yetkili olduğu ortamları gösterir;
kullanıcı ortamı seçip "Uygulamaya gir" der. "Çıkış" yalnızca uygulamadan çıkarır, Windows oturumu açık kalır.

## Ortamlar (DEV / TEST / PROD)

Birden fazla BO ortamı `SapBo:Environments` altında tanımlanır. Kullanıcı sağ üstteki **Ortam** menüsünden
yetkili olduğu ortamlar arasında geçer; seçim cookie'de saklanır. Aktif ortam rengi menünün altındaki şeritte
görünür (`Color: danger` = kırmızı, PROD için önerilir).

Her ortamın **kendi yetkileri** vardır (`SapBo:Environments:{ORTAM}:Authorization:{Modül}`).
Kullanıcı bir ortamda yalnızca o ortamın sicil/grup listesine uyuyorsa işlem yapabilir.

## Şifreler

BO servis hesabı şifreleri **asla** `appsettings.json`'a yazılmaz.

- Geliştirme: `BoAdmin.Web` > sağ tık > **Manage User Secrets**
  ```json
  { "SapBo": { "Environments": { "TEST": { "Password": "..." }, "PROD": { "Password": "..." } } } }
  ```
- Prod (IIS app pool ortam değişkeni): `SapBo__Environments__PROD__Password`, `SapBo__Environments__TEST__Password`

Şifresi tanımlı olmayan ortama geçilirse uygulama açılır, ilk BO işleminde açık hata mesajı gösterilir.

## Audit

Kayıtlar aylık JSON Lines dosyalarına yazılır: `Audit:Directory` altında `audit-2026-11.jsonl`
(ortam, kullanıcı, işlem, rapor ID/CUID/ad/klasör, hedef klasör, sonuç). Geliştirmede `logs/audit`,
prod'da `D:\Logs\BoAdmin\audit`. IIS app pool kimliğine bu klasörde **Modify** yetkisi verilmeli.

## `SapBo` ayarları

| Anahtar | Açıklama |
|---|---|
| `DefaultEnvironment` | İlk açılışta seçili ortam |
| `TokenLifetimeMinutes` | Token bu süreden sonra yenilenir (BO oturum süresinden kısa olmalı) |
| `Environments:{ORTAM}:BaseUrl` | `http://<sunucu>:6405/biprws` |
| `Environments:{ORTAM}:AuthType` | `secEnterprise` / `secWinAD` / `secLDAP` |
| `Environments:{ORTAM}:IsCritical` | `true`: silme / yedekleme / ad güncelleme onayında ek "Emin misiniz?" penceresi ve kırmızı uyarı. Yazılmazsa `Color: danger` olan ortam kritik sayılır |
| `Environments:{ORTAM}:RootFolderId` | Public Folders kökünün `SI_ID`'si (standart: 23) |
| `Environments:{ORTAM}:ArchiveFolderId` | "Kurtarılabilir silme" arşiv klasörünün `SI_ID`'si (0 = kapalı) |
| `Environments:{ORTAM}:DeleteStrategy` | İlk denenecek silme uç noktası: `Documents` (`DELETE /v1/documents/{id}`), `InfoObjects` veya `Raylight`. 404 dönerse diğerleri otomatik denenir |
| `Environments:{ORTAM}:MoveStrategy` | `InfoObjects` (`PUT /v1/infoobjects/{id}`) veya `Raylight` |
| `Environments:{ORTAM}:Authorization` | Modül bazlı `AllowedUsers` / `AllowedGroups` |

## Rapor Yönetişimi

- **Rapor Arama:** ilk açılışta liste yüklenmez (hızlı filtreler veya "Ara" ile başlar), 20 kayıtlık sayfalar, klasör otomatik tamamlama; filtre, sıralama, sayfalama; tekli/çoklu silme veya arşive taşıma (önizleme + sayı ile onay)
- **Rapor Silme (TXT ile):** CUID listesi yüklenir, yedekleme klasörü ID'si girilir. Her rapor yedekleme klasörüne
  `{rapor adı}_{orijinal CUID}` adıyla kopyalanır (Raylight, yalnızca Web Intelligence), kopya doğrulanınca
  orijinal silinir. Kopya oluşmazsa orijinale dokunulmaz; yeniden çalıştırmada var olan yedek tekrar kopyalanmaz.
  Kopya yeni CUID alır, instance ve zamanlamalar kopyalanmaz. En fazla 500 CUID.
- **Rapor Adı Güncelleme (Excel):** CUID + yeni ad içeren .xlsx yüklenir (şablon indirilebilir), önizlemede eski/yeni
  adlar görülür ve sayı yazılarak onaylanır. Ad, `GET + PUT /v1/documents/{id}` (olmazsa `/v1/infoobjects/{id}`) ile
  güncellenir ve CMS sorgusuyla doğrulanır. CUID, klasör, instance ve zamanlamalar değişmez. En fazla 500 rapor.
- **Keyword Güncelleme (Excel):** CUID + keyword + İşlem (Ekle / Değiştir) içeren .xlsx yüklenir (şablon indirilebilir).
  Önizlemede mevcut ve yeni keyword'ler (eklenen/kaldırılan) görülür. Keyword'ler `GET + PUT /v1/documents/{id}` ile
  yazılır ve yeniden okunarak doğrulanır. "Ekle"de sonuç işlem anındaki güncel keyword'lerden hesaplanır.
- **İşlem Geçmişi:** ortam bazlı filtre, CSV dışa aktarma

## Sbox ile şifre alma

BO servis kullanıcısı ve şifresi ortam bazında şirket şifre kasası Sbox'tan (HsmService) alınabilir.

1. Proxy'yi üretin (geliştirme makinesinde bir kez): `tools\generate-sbox-proxy.bat "http://SBOX-SUNUCU/HsmService.svc?wsdl"`.
   Çıktı `src\BoAdmin.Infrastructure\Sbox\HsmServiceProxy.cs` (gitignore'da). Dosya yokken proje derlenir ama Sbox kullanılamaz.
2. `appsettings.json` > `SapBo:Sbox`: `Endpoint`, `UserName`. Şifre: User Secrets `SapBo:Sbox:Password` / sunucuda `.\sifre-ayarla.ps1 -Ortam SBOX`.
3. Sbox'tan alınacak ortama `"Sbox": { "AppName", "SubAppName", "Ip", "SensitiveDataName" }` ekleyin. Kayıttaki değer `kullanici;sifre` biçiminde olmalı.
4. Bilgi `CacheMinutes` boyunca bellekte tutulur; BO "şifre hatalı" derse Sbox'tan yeniden alınıp bir kez daha denenir.

## Sunucuda exe olarak çalıştırma (IIS gerekmez)

1. VS: Build > Publish > **Sunucu** profili > Publish. Çıktı: `src\BoAdmin.Web\bin\publish\` (self-contained, sunucuya .NET kurmak gerekmez).
2. Klasörün tamamını sunucuya kopyalayın (ör. `D:\Apps\BoAdmin`).
3. Port: `appsettings.Production.json` > `Kestrel:Endpoints:Http:Url` (varsayılan `http://0.0.0.0:5080`).
4. Log/audit klasörü: `appsettings.json` > `Serilog...path` ve `Audit:Directory` sunucuda var olan bir sürücüyü göstermeli.
5. Yönetici PowerShell'de: `.\servis-kur.ps1 -Port 5080`, ardından her ortam için `.\sifre-ayarla.ps1 -Ortam TEST`.
6. Erişim: `http://SUNUCUADI:5080`. Deneme için servis kurmadan `BoAdmin.exe` çift tıkla da çalışır.

Güncelleme: servisi durdurun (`Stop-Service BoAdmin`), dosyaları üzerine kopyalayın (`appsettings*.json` hariç), başlatın.

## Kod parçaları (ilerleme)

- [x] Parça 1: Proje dosyaları, `Program.cs`, `appsettings*.json`
- [x] Parça 2: Yetkilendirme (handler, policy, menü erişimi)
- [x] Parça 3: BO oturum servisi + REST client
- [x] Parça 4: Rapor gateway + rapor servisi
- [x] Parça 5: Audit log - dosya tabanlı (+ hata filtresi, view konumları)
- [x] Parça 6: Controller'lar + view'lar + layout
- [x] Ek: Çok ortam desteği + TXT ile toplu silme
- [ ] Parça 7: IIS kurulumu (`web.config`, adımlar)
