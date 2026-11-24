@echo off
rem ------------------------------------------------------------------
rem  Sbox (HsmService) WCF istemci sınıflarını WSDL'den üretir (.NET 8).
rem  Çıktı: src\BoAdmin.Infrastructure\Sbox\HsmServiceProxy.cs
rem         (namespace BoAdmin.Infrastructure.Sbox)
rem
rem  Kullanım:
rem    tools\generate-sbox-proxy.bat <WSDL adresi veya dosya yolu>
rem  Örnek:
rem    tools\generate-sbox-proxy.bat "http://SBOX-SUNUCU/HsmService.svc?wsdl"
rem    tools\generate-sbox-proxy.bat C:\dev\libs\sbox\HsmService.wsdl
rem
rem  Üretilen dosya .gitignore'da; her geliştirme makinesinde bir kez çalıştırmanız yeterli.
rem  Dosya varken derlenen exe Sbox'ı kullanabilir (sunucuya ayrıca bir şey kurulmaz).
rem ------------------------------------------------------------------
setlocal

if "%~1"=="" (
  echo Kullanim: %~nx0 ^<WSDL adresi veya dosya yolu^>
  exit /b 2
)

set "OUTDIR=%~dp0..\src\BoAdmin.Infrastructure\Sbox"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

where dotnet-svcutil >nul 2>nul
if errorlevel 1 (
  echo dotnet-svcutil bulunamadi, kuruluyor...
  dotnet tool install --global dotnet-svcutil
  if errorlevel 1 (
    echo [HATA] dotnet-svcutil kurulamadi. NuGet erisimini kontrol edin
    echo        veya README'deki "Visual Studio ile uretme" adimlarini izleyin.
    exit /b 1
  )
  set "PATH=%PATH%;%USERPROFILE%\.dotnet\tools"
)

rem Proje dosyasına dokunmadan yalnızca proxy dosyasını üret
pushd "%OUTDIR%"
dotnet-svcutil "%~1" --namespace "*,BoAdmin.Infrastructure.Sbox" --outputFile HsmServiceProxy.cs --outputDir . --noLogo --targetFramework net8.0
set "RC=%ERRORLEVEL%"
popd

if not "%RC%"=="0" (
  echo [HATA] Proxy uretilemedi.
  exit /b 1
)

rem dotnet-svcutil yan dosyalar bırakabilir; derlemeye karışmasın
if exist "%OUTDIR%\dotnet-svcutil.params.json" del /q "%OUTDIR%\dotnet-svcutil.params.json"

echo [OK] Uretildi: %OUTDIR%\HsmServiceProxy.cs
echo Visual Studio'da cozumu yeniden derleyin (Rebuild).
exit /b 0
