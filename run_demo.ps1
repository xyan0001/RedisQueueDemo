# Run Redis Terminal Management Demo
# This script runs the CacheTestApp to demonstrate terminal lifecycle
# Author: GitHub Copilot
# Date: June 8, 2025

Write-Host "Terminal Management Demo Script" -ForegroundColor Cyan
Write-Host "===========================" -ForegroundColor Cyan
Write-Host ""

# Ensure Redis is running
$redisRunning = $false
try {
    # Check if Redis is running using redis-cli ping
    $pingStat = Invoke-Expression "redis-cli ping"
    if ($pingStat -eq "PONG") {
        $redisRunning = $true
        Write-Host "✓ Redis server is running" -ForegroundColor Green
    }
}
catch {
    Write-Host "✗ Redis server not detected" -ForegroundColor Red
}

if (-not $redisRunning) {
    Write-Host "Would you like to start Redis server? (y/n)"
    $startRedis = Read-Host
    if ($startRedis -eq "y") {
        Write-Host "Starting Redis server..."
        Start-Process "redis-server" -NoNewWindow
        Start-Sleep -Seconds 2
        Write-Host "Redis server started"
    }
    else {
        Write-Host "Redis is required for this demo. Exiting..."
        Exit
    }
}

# Option to clear Redis data before running the demo
Write-Host ""
Write-Host "Would you like to clear Redis data before running the demo? (y/n)"
$clearRedis = Read-Host
if ($clearRedis -eq "y") {
    Write-Host "Clearing Redis data..." -ForegroundColor Yellow
    Invoke-Expression "redis-cli FLUSHDB"
    Write-Host "Redis data cleared"
}

# Show current terminal pool contents
Write-Host ""
Write-Host "Current terminals in pool:" -ForegroundColor Cyan
Invoke-Expression "redis-cli SMEMBERS terminal_pool"

# Run the CacheTestApp
Write-Host ""
Write-Host "Running Terminal Management Demo..." -ForegroundColor Green
Write-Host "===============================" -ForegroundColor Green
Set-Location "d:\dev\RedisQueueDemo"
dotnet run --project CacheTestApp/CacheTestApp.csproj

Write-Host ""
Write-Host "Demo completed. Displaying Redis state:" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan

# Display Redis state after demo
Write-Host ""
Write-Host "Terminals in pool:" -ForegroundColor Yellow
Invoke-Expression "redis-cli SMEMBERS terminal_pool"

Write-Host ""
Write-Host "Active terminal sessions:" -ForegroundColor Yellow
Invoke-Expression "redis-cli KEYS terminal:session:*"

Write-Host ""
Write-Host "Terminal statuses:" -ForegroundColor Yellow
$statusKeys = Invoke-Expression "redis-cli KEYS terminal:status:*"
foreach ($key in $statusKeys) {
    Write-Host "Status for $key:" -ForegroundColor Cyan
    Invoke-Expression "redis-cli HGETALL $key"
    Write-Host ""
}

Write-Host "Demo finished. Press any key to exit..."
Read-Host
