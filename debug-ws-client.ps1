# Simulates exactly what the Android app does: WebSocket connect + hello handshake
# Run while PadlinkNes.exe is running

param(
    [string]$Url = "ws://127.0.0.1:8787/",
    [string]$Role = "player1"
)

Write-Host "`n=== WebSocket Handshake Test ===" -ForegroundColor Cyan
Write-Host "Target : $Url"
Write-Host "Role   : $Role`n"

Add-Type -AssemblyName System.Net.WebSockets

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(8000)  # 8-second overall timeout

try {
    # Step 1: TCP + WebSocket upgrade
    Write-Host "[1] Connecting..." -NoNewline
    $connectTask = $ws.ConnectAsync([Uri]$Url, $cts.Token)
    $connectTask.Wait($cts.Token)
    Write-Host " OK  (state: $($ws.State))" -ForegroundColor Green

    # Step 2: Send hello JSON (matches WebSocketManager.kt exactly)
    $hello = '{"type":"hello","role":"' + $Role + '"}'
    Write-Host "[2] Sending hello: $hello" -NoNewline
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($hello)
    $segment = [System.ArraySegment[byte]]::new($bytes)
    $sendTask = $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token)
    $sendTask.Wait($cts.Token)
    Write-Host " OK" -ForegroundColor Green

    # Step 3: Read server reply
    Write-Host "[3] Waiting for hello_ack..." -NoNewline
    $buf = New-Object byte[] 512
    $recvSeg = [System.ArraySegment[byte]]::new($buf)
    $result = $ws.ReceiveAsync($recvSeg, $cts.Token).GetAwaiter().GetResult()
    $reply = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
    Write-Host " Got: $reply" -ForegroundColor Green

    if ($reply -match '"hello_ack"') {
        Write-Host "`nSUCCESS - server accepted the connection. Phone issue is network/WiFi." -ForegroundColor Green
    } elseif ($reply -match '"hello_rejected"') {
        Write-Host "`nSERVER REJECTED - role may already be taken. Try restarting NesReceiver." -ForegroundColor Yellow
    }

    # Clean close
    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "test done", [System.Threading.CancellationToken]::None).Wait(2000) | Out-Null

} catch [System.OperationCanceledException] {
    Write-Host "`nTIMED OUT - server did not respond within 8 seconds" -ForegroundColor Red
    Write-Host "WS state: $($ws.State)"
} catch {
    $inner = $_.Exception.InnerException
    $msg = if ($inner) { $inner.Message } else { $_.Exception.Message }
    Write-Host "`nFAILED - $msg" -ForegroundColor Red
    Write-Host "WS state: $($ws.State)"
    if ($msg -match 'refused') {
        Write-Host "  --> Nothing listening on that address/port" -ForegroundColor Yellow
    }
} finally {
    $ws.Dispose()
    $cts.Dispose()
}

Write-Host ""
