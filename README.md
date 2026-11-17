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
| Rapor İşlemleri (`Reports`) | Geliştiriliyor |
| Kullanıcı İşlemleri (`Users`) | Yakında |
| Grup İşlemleri (`Groups`) | Yakında |

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
| `Environments:{ORTAM}:RootFolderId` | Public Folders kökünün `SI_ID`'si (standart: 23) |
| `Environments:{ORTAM}:ArchiveFolderId` | "Kurtarılabilir silme" arşiv klasörünün `SI_ID`'si (0 = kapalı) |
| `Environments:{ORTAM}:DeleteStrategy` | `InfoObjects` (`DELETE /v1/infoobjects/{id}`) veya `Raylight` (yalnızca Webi) |
| `Environments:{ORTAM}:MoveStrategy` | `InfoObjects` (`PUT /v1/infoobjects/{id}`) veya `Raylight` |
| `Environments:{ORTAM}:Authorization` | Modül bazlı `AllowedUsers` / `AllowedGroups` |

## Rapor İşlemleri

- **Rapor Arama:** filtre, sıralama, sayfalama; tekli/çoklu silme veya arşive taşıma (önizleme + sayı ile onay)
- **Toplu Silme (TXT):** CUID listesi yüklenir, yedekleme klasörü ID'si girilir. Her rapor yedekleme klasörüne
  `{rapor adı}_{orijinal CUID}` adıyla kopyalanır (Raylight, yalnızca Web Intelligence), kopya doğrulanınca
  orijinal silinir. Kopya oluşmazsa orijinale dokunulmaz; yeniden çalıştırmada var olan yedek tekrar kopyalanmaz.
  Kopya yeni CUID alır, instance ve zamanlamalar kopyalanmaz. En fazla 500 CUID.
- **İşlem Geçmişi:** ortam bazlı filtre, CSV dışa aktarma

## Kod parçaları (ilerleme)

- [x] Parça 1: Proje dosyaları, `Program.cs`, `appsettings*.json`
- [x] Parça 2: Yetkilendirme (handler, policy, menü erişimi)
- [x] Parça 3: BO oturum servisi + REST client
- [x] Parça 4: Rapor gateway + rapor servisi
- [x] Parça 5: Audit log - dosya tabanlı (+ hata filtresi, view konumları)
- [x] Parça 6: Controller'lar + view'lar + layout
- [x] Ek: Çok ortam desteği + TXT ile toplu silme
- [ ] Parça 7: IIS kurulumu (`web.config`, adımlar)
