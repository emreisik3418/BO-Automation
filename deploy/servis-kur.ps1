<#
  BoAdmin'i Windows servisi olarak kurar, güvenlik duvarında portu açar ve servisi başlatır.
  Kullanım (yönetici PowerShell, BoAdmin.exe ile aynı klasörde):
      .\servis-kur.ps1                 # port 5080
      .\servis-kur.ps1 -Port 8080      # farklı port (appsettings.Production.json'daki ile aynı olmalı)
#>
param(
    [int]$Port = 5080,
    [string]$ServiceName = "BoAdmin"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Bu betiği 'Yönetici olarak çalıştır' ile açılmış PowerShell'de çalıştırın."
}

$exe = Join-Path $PSScriptRoot "BoAdmin.exe"
if (-not (Test-Path $exe)) { throw "BoAdmin.exe bulunamadı: $exe. Betiği yayın klasörüne kopyalayın." }

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Servis zaten var, durdurulup yeniden kuruluyor..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Servis: otomatik başlar, çökerse 1 dk sonra yeniden başlar
sc.exe create $ServiceName binPath= "`"$exe`"" start= delayed-auto DisplayName= "BoAdmin - SAP BO Yönetim" | Out-Null
sc.exe description $ServiceName "SAP BusinessObjects yönetim uygulaması (http://$($env:COMPUTERNAME):$Port)" | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# Güvenlik duvarı
$ruleName = "BoAdmin TCP $Port"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Domain,Private | Out-Null
    Write-Host "Güvenlik duvarı kuralı eklendi: $ruleName"
}

Start-Service -Name $ServiceName
Start-Sleep -Seconds 3
Get-Service -Name $ServiceName | Format-Table Name, Status, StartType -AutoSize

Write-Host ""
Write-Host "Adres: http://$($env:COMPUTERNAME):$Port" -ForegroundColor Green
Write-Host "Sorun olursa log klasörüne bakın (appsettings.json > Serilog > path)."
