# Padlink NES - Connection Diagnostic Script
# Run this on the PC WHILE PadlinkNes.exe is running
# It checks what's actually listening on port 8787 and tests a WebSocket handshake

$port = 8787

Write-Host "`n=== Padlink NES Connection Diagnostics ===" -ForegroundColor Cyan

# 1. Check if anything is listening on port 8787
Write-Host "`n[1] Processes listening on port $port :" -ForegroundColor Yellow
$listeners = netstat -ano | Select-String ":$port\s"
if ($listeners) {
    $listeners | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host "  NOTHING is listening on port $port - the receiver is not running or failed to bind!" -ForegroundColor Red
}

# 2. Show all local IP addresses the phone could connect to
Write-Host "`n[2] Local network interfaces (potential receiver addresses):" -ForegroundColor Yellow
Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } |
    ForEach-Object { Write-Host "  $($_.IPAddress)  (adapter: $($_.InterfaceAlias))" }

# 3. Check Windows Firewall for port 8787
Write-Host "`n[3] Windows Firewall rules for port $port :" -ForegroundColor Yellow
$fwRules = Get-NetFirewallPortFilter -Protocol TCP |
    Where-Object { $_.LocalPort -eq $port -or $_.LocalPort -eq 'Any' } |
    Get-NetFirewallRule |
    Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' }

if ($fwRules) {
    $fwRules | ForEach-Object { Write-Host "  [$($_.Action)] $($_.DisplayName)" -ForegroundColor Green }
} else {
    Write-Host "  No inbound firewall rule found for TCP $port" -ForegroundColor Red
    Write-Host "  --> The Windows Firewall may be BLOCKING the phone's connection!" -ForegroundColor Red
    Write-Host "  To add a rule run (as Administrator):" -ForegroundColor Yellow
    Write-Host "    netsh advfirewall firewall add rule name=`"Padlink NES`" protocol=TCP dir=in localport=$port action=allow" -ForegroundColor White
}

# 4. Try a raw HTTP GET to ws://localhost:8787 (should get 400, not a connection refused)
Write-Host "`n[4] Testing HTTP reachability on localhost:$port ..." -ForegroundColor Yellow
try {
    $resp = Invoke-WebRequest -Uri "http://localhost:$port/" -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
    Write-Host "  Got HTTP $($resp.StatusCode) - server responded (expected 400 for non-WS request)" -ForegroundColor Green
} catch {
    $status = $_.Exception.Response?.StatusCode
    if ($status) {
        Write-Host "  Got HTTP $([int]$status) - server is up and responding" -ForegroundColor Green
    } elseif ($_.Exception.Message -match 'refused|Unable to connect') {
        Write-Host "  CONNECTION REFUSED - nothing is accepting connections on localhost:$port" -ForegroundColor Red
    } elseif ($_.Exception.Message -match 'timed out|timeout') {
        Write-Host "  TIMED OUT - port may be firewalled or not bound" -ForegroundColor Red
    } else {
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 5. Check URL ACL reservations
Write-Host "`n[5] URL ACL reservations for port $port :" -ForegroundColor Yellow
$urlacl = netsh http show urlacl | Select-String -Context 0,1 "8787"
if ($urlacl) {
    $urlacl | ForEach-Object { Write-Host "  $($_.Line)" }
    $urlacl | ForEach-Object { Write-Host "  $($_.Context.PostContext)" }
} else {
    Write-Host "  No URL ACL found for port $port" -ForegroundColor Red
    Write-Host "  Run from elevated prompt: netsh http add urlacl url=http://+:8787/ user=Everyone" -ForegroundColor Yellow
}

Write-Host "`n=== Done. Share this output to diagnose the issue. ===" -ForegroundColor Cyan
