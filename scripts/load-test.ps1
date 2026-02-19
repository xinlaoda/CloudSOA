#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA 简单负载测试脚本
.DESCRIPTION
    向 Broker 并发发送请求并统计吞吐量
.EXAMPLE
    .\scripts\load-test.ps1
    .\scripts\load-test.ps1 -BrokerUrl http://myhost:5000 -TotalRequests 500 -Concurrency 10
#>

[CmdletBinding()]
param(
    [string]$BrokerUrl     = 'http://localhost:5000',
    [int]$TotalRequests    = 100,
    [int]$Concurrency      = 5
)

$ErrorActionPreference = 'Stop'

$BatchSize = [Math]::Floor($TotalRequests / $Concurrency)
$Payload   = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('load-test-payload-data-1234567890'))

Write-Host '============================================'
Write-Host '  CloudSOA 负载测试'
Write-Host "  Broker:   $BrokerUrl"
Write-Host "  总请求:    $TotalRequests"
Write-Host "  并发:      $Concurrency"
Write-Host "  每批:      $BatchSize"
Write-Host '============================================'
Write-Host ''

# 1. 创建 Session
Write-Host '[1] 创建 Session...'
$session = Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions" -Method Post `
    -ContentType 'application/json' `
    -Body '{"serviceName":"LoadTestService","minimumUnits":1,"maximumUnits":50}'
$SessionId = $session.sessionId
Write-Host "    SessionId: $SessionId"

# 2. 构建请求 payload
$requests = @()
for ($i = 1; $i -le $BatchSize; $i++) {
    $requests += @{ action = 'Echo'; payload = $Payload; userData = "$i" }
}
$batchBody = @{ requests = $requests } | ConvertTo-Json -Depth 3 -Compress

# 3. 并发发送
Write-Host "[2] 发送 $TotalRequests 请求 ($Concurrency 并发)..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$jobs = @()
for ($c = 0; $c -lt $Concurrency; $c++) {
    $jobs += Start-ThreadJob -ScriptBlock {
        param($url, $sid, $body)
        Invoke-RestMethod -Uri "$url/api/v1/sessions/$sid/requests" `
            -Method Post -ContentType 'application/json' -Body $body | Out-Null
    } -ArgumentList $BrokerUrl, $SessionId, $batchBody
}

$jobs | Wait-Job | Out-Null
$jobs | Remove-Job

$sendMs = $sw.ElapsedMilliseconds
Write-Host "    发送耗时: ${sendMs}ms"

# Flush
Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions/$SessionId/requests/flush" -Method Post | Out-Null

# 4. 等待处理并拉取响应
Write-Host '[3] 等待处理并拉取响应...'
$received  = 0
$timeout   = 60
$waitStart = [DateTime]::UtcNow

while ($received -lt $TotalRequests) {
    $elapsed = ([DateTime]::UtcNow - $waitStart).TotalSeconds
    if ($elapsed -gt $timeout) {
        Write-Host "    ⚠ 超时 (${timeout}s)，已接收 $received/$TotalRequests" -ForegroundColor Yellow
        break
    }

    try {
        $resp = Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions/$SessionId/responses?maxCount=500"
        $batchCount = [int]$resp.count
        $received += $batchCount

        if ($batchCount -eq 0) { Start-Sleep -Milliseconds 500 }
    } catch {
        Start-Sleep -Milliseconds 500
    }
}

$sw.Stop()
$totalMs = $sw.ElapsedMilliseconds

# 5. 关闭 Session
Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions/$SessionId" -Method Delete -ErrorAction SilentlyContinue | Out-Null

# 6. 输出结果
Write-Host ''
Write-Host '============================================'
Write-Host '  负载测试结果'
Write-Host '============================================'
Write-Host "  总请求:      $TotalRequests"
Write-Host "  已接收响应:   $received"
Write-Host "  总耗时:       ${totalMs}ms"

if ($totalMs -gt 0) {
    $tps = [Math]::Floor($received * 1000 / $totalMs)
    Write-Host "  吞吐量:       $tps req/s"
}

if ($received -lt $TotalRequests) {
    $loss = $TotalRequests - $received
    $pct  = [Math]::Floor($loss * 100 / $TotalRequests)
    Write-Host "  丢失:         $loss (${pct}%)"
}

Write-Host '============================================'
