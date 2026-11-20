<#
  BoAdmin servisini durdurur ve kaldırır. Dosyalara ve loglara dokunmaz.
  Kullanım (yönetici PowerShell):  .\servis-kaldir.ps1
#>
param(
    [string]$ServiceName = "BoAdmin",
    [int]$Port = 5080
)

$ErrorActionPreference = "Stop"

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Servis kaldırıldı: $ServiceName"
} else {
    Write-Host "Servis bulunamadı: $ServiceName"
}

$ruleName = "BoAdmin TCP $Port"
if (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue) {
    Remove-NetFirewallRule -DisplayName $ruleName
    Write-Host "Güvenlik duvarı kuralı kaldırıldı: $ruleName"
}
