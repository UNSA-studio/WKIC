# ============================================================================
#  Windows Keyboard Integrity Check —— 代码签名脚本
#
#  说明：本脚本使用的是「自签名证书」，不是任何厂商的官方证书。
#        它能给 exe 加上 Authenticode 签名，但只有在导入了公钥证书的机器上
#        才会显示为「有效签名」；在其它机器上仍会显示「未知发布者」。
#
#  为什么不能套用微软的签名：见 README 的「关于软件签名」一节。
#
#  用法：
#      powershell -ExecutionPolicy Bypass -File tools\sign.ps1
#      powershell -ExecutionPolicy Bypass -File tools\sign.ps1 -Trust
#
#  -Trust  额外把自签名证书导入当前用户的「受信任的根证书颁发机构」与
#          「受信任的发布者」，这样本机的 SmartScreen 不会再拦自己的程序。
# ============================================================================

#Requires -Version 5.0

param(
    [string]$Exe = "$PSScriptRoot\..\build\WindowsKeyboardIntegrityCheck.exe",
    [string]$Subject = "CN=UNSA-studio Code Signing, O=UNSA-studio, C=CN",
    [switch]$Trust
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "==========================================================" -ForegroundColor DarkCyan
Write-Host "  Windows Keyboard Integrity Check - 自签名工具" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor DarkCyan
Write-Host ""

if (-not (Test-Path -LiteralPath $Exe)) {
    throw "找不到可执行文件：$Exe`n请先运行 build.bat 生成 exe。"
}
$Exe = (Resolve-Path -LiteralPath $Exe).Path

# ---------- 1. 准备证书 ----------
Write-Host "[1/3] 准备代码签名证书 ..." -ForegroundColor Cyan

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $Subject } |
        Select-Object -First 1

if ($null -eq $cert) {
    Write-Host "      未找到，正在创建新的自签名证书 ..."
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5) `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA `
        -KeyLength 3072
    Write-Host "      已创建，指纹：$($cert.Thumbprint)" -ForegroundColor Green
} else {
    Write-Host "      复用已有证书，指纹：$($cert.Thumbprint)" -ForegroundColor Green
}

# ---------- 2. 导出公钥证书 ----------
$cerPath = Join-Path (Split-Path -Parent $Exe) "WKIC-selfsigned.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "      公钥证书已导出：$cerPath"

# ---------- 3. 签名 ----------
Write-Host ""
Write-Host "[2/3] 对 exe 签名 ..." -ForegroundColor Cyan

$sig = Set-AuthenticodeSignature -FilePath $Exe -Certificate $cert -HashAlgorithm SHA256
Write-Host "      状态：$($sig.Status)" -ForegroundColor Green
Write-Host "      签名者：$($sig.SignerCertificate.Subject)"

# ---------- 4. 可选：本机信任 ----------
if ($Trust) {
    Write-Host ""
    Write-Host "[3/3] 导入本机受信任存储 ..." -ForegroundColor Yellow
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\Root            | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
    Write-Host "      已导入（仅当前用户）。重新验证签名 ..."

    $sig2 = Get-AuthenticodeSignature -FilePath $Exe
    Write-Host "      状态：$($sig2.Status)" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[3/3] 跳过本机信任导入（如需，请加 -Trust 参数）" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "完成。注意：自签名只在导入了 $cerPath 的机器上被视为有效。" -ForegroundColor Yellow
Write-Host "要让所有用户都看到可信的发布者，需要购买正规 CA 签发的代码签名证书。" -ForegroundColor Yellow
Write-Host ""