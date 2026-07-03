# TradeXpress DB yedekleme — SQLEXPRESS'te SQL Agent YOK → bu script Windows Task Scheduler ile GÜNLÜK çalıştırılır.
# Kurulum (yönetici PowerShell, tek sefer):
#   $act = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -File `"$PSScriptRoot\backup-database.ps1`""
#   $trg = New-ScheduledTaskTrigger -Daily -At 02:00
#   Register-ScheduledTask -TaskName "TradeXpress-DB-Backup" -Action $act -Trigger $trg -RunLevel Highest
# FULL recovery model + LOG yedeği için ayrıca 15 dk'da bir LOG job'ı ekle (aşağıda not).

param(
    [string]$Server   = ".\SQLEXPRESS",
    [string]$Database = "TradeXpress",
    [string]$BackupDir = "D:\Backups\TradeXpress",   # OFF-SITE/farklı disk olsun (aynı disk = tek nokta arıza)
    [int]$RetentionDays = 30
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null }
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$file  = Join-Path $BackupDir "$($Database)_FULL_$stamp.bak"

# FULL yedek (COMPRESSION + CHECKSUM). -E: Windows auth (SA parolası script'e girmez). Gerekirse -U/-P kullan.
$sql = "BACKUP DATABASE [$Database] TO DISK = N'$file' WITH FORMAT, INIT, CHECKSUM, COMPRESSION, STATS = 10;"
sqlcmd -S $Server -E -b -Q $sql
if ($LASTEXITCODE -ne 0) { throw "BACKUP başarısız (exit $LASTEXITCODE)" }

# Restore edilebilirliği DOĞRULA (yedek bozuksa erken yakala).
sqlcmd -S $Server -E -b -Q "RESTORE VERIFYONLY FROM DISK = N'$file' WITH CHECKSUM;"
if ($LASTEXITCODE -ne 0) { throw "RESTORE VERIFYONLY başarısız — yedek bozuk!" }

# Eski yedekleri temizle (retention).
Get-ChildItem $BackupDir -Filter "$($Database)_FULL_*.bak" |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$RetentionDays) } |
    Remove-Item -Force

Write-Host "OK: $file (doğrulandı). $RetentionDays günden eski yedekler temizlendi."

# NOT — Felaket kurtarma tam kapsamı için:
#  1) DB'yi FULL recovery model'e al: ALTER DATABASE [TradeXpress] SET RECOVERY FULL;
#  2) 15 dk'da bir LOG yedeği (ayrı Task): BACKUP LOG [TradeXpress] TO DISK='...\..._LOG_<stamp>.trn';
#  3) $BackupDir'i OFF-SITE (başka makine/bulut) senkronla.
#  4) Ayda bir GERÇEK restore testi (yedek var ≠ restore çalışır).
