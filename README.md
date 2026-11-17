# BO-Automation (BoAdmin)

SAP BusinessObjects 4.3 yönetim web uygulaması.
ASP.NET Core MVC (.NET 8), Windows Authentication, BI Platform RESTful Web Services (`/biprws`).

## Proje yapısı

```
src/
├─ BoAdmin.Web/             # MVC: Controller, View, yetkilendirme, layout
├─ BoAdmin.Application/     # Servisler, DTO'lar, arayüzler (BO'dan bağımsız)
└─ BoAdmin.Infrastructure/  # BO REST client, oturum yönetimi, audit log (EF Core)
```

Bağımlılık yönü: `Web → Application ← Infrastructure` (Web, composition root olarak Infrastructure'ı da referanslar).

## Modüller

| Modül | Durum |
|---|---|
| Rapor İşlemleri (`Reports`) | Geliştiriliyor |
| Kullanıcı İşlemleri (`Users`) | Yakında |
| Grup İşlemleri (`Groups`) | Yakında |

## Geliştirme ortamı

1. BO servis hesabı şifresi **asla** `appsettings.json`'a yazılmaz.
   Visual Studio'da `BoAdmin.Web` > sağ tık > **Manage User Secrets**:
   ```json
   { "SapBo": { "Password": "..." } }
   ```
2. Prod ortamında ortam değişkeni: `SapBo__Password` (IIS app pool ortam değişkeni).
3. Audit veritabanı geliştirmede LocalDB (`BoAdmin_Dev`), ilk çalıştırmada otomatik oluşturulur.

## `SapBo` ayarları

| Anahtar | Açıklama |
|---|---|
| `BaseUrl` | `http://<sunucu>:6405/biprws` |
| `AuthType` | `secEnterprise` / `secWinAD` / `secLDAP` |
| `TokenLifetimeMinutes` | Token bu süreden sonra yenilenir (BO oturum süresinden kısa olmalı) |
| `ArchiveFolderId` | "Kurtarılabilir silme" için arşiv klasörünün `SI_ID`'si (0 = kapalı) |
| `DeleteStrategy` | `InfoObjects` (`DELETE /v1/infoobjects/{id}`) veya `Raylight` (yalnızca Webi) |
| `MoveStrategy` | `InfoObjects` (`PUT /v1/infoobjects/{id}`) veya `Raylight` |

## Kod parçaları (ilerleme)

- [x] Parça 1: Proje dosyaları, `Program.cs`, `appsettings*.json`
- [x] Parça 2: Yetkilendirme (handler, policy, menü erişimi)
- [x] Parça 3: BO oturum servisi + REST client
- [x] Parça 4: Rapor gateway + rapor servisi
- [x] Parça 5: Audit log (+ hata filtresi, view konumları)
- [ ] Parça 6: Controller'lar + view'lar + layout
- [ ] Parça 7: IIS kurulumu (`web.config`, adımlar)
