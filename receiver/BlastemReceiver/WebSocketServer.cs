using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlastemReceiver;

/// <summary>
/// WebSocket server that accepts Sega Genesis controller connections from Android phones.
///
/// Wire protocol:
///   - After TCP connect the phone sends a JSON text frame:
///       { "type": "hello", "role": "player1" | "player2" }
///   - Server replies with hello_ack or hello_rejected.
///   - Subsequent frames are binary, 2-byte bitmask of button states:
///       Byte 0 (low): Up(0), Down(1), Left(2), Right(3), A(4), B(5), C(6), Start(7)
///       Byte 1 (high): X(0), Y(1), Z(2), Mode(3)
/// </summary>
internal sealed class WebSocketServer
{
    private const int MaxConnections = 2;
    private const int ReceiveBufferSize = 256;

    private readonly int _port;
    private readonly MainForm _form;
    private HttpListener? _listener;
    private CancellationTokenSource _cts = new();

    private readonly object _rolesLock = new();
    private string? _p1Role = null;
    private string? _p2Role = null;

    private string _p1Status = "Not connected";
    private string _p2Status = "Not connected";

    public WebSocketServer(int port, MainForm form)
    {
        _port = port;
        _form = form;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_port}/");

        try
        {
            _listener.Start();
            _form.Log($"Server started on ws://+:{_port}/");
        }
        catch (HttpListenerException ex)
        {
            _form.Log($"Warning: Failed to bind to all interfaces (ws://+:{_port}/). Error: {ex.Message}");
            _listener.Prefixes.Clear();
            string lanIp = NetworkHelper.GetLocalIpAddress();
            string prefixLan   = $"http://{lanIp}:{_port}/";
            string prefixLocal = $"http://127.0.0.1:{_port}/";
            _listener.Prefixes.Add(prefixLan);
            _listener.Prefixes.Add(prefixLocal);
            try
            {
                _listener.Start();
                _form.Log($"Server listening fallback: {prefixLan.Replace("http://", "ws://")} and {prefixLocal.Replace("http://", "ws://")}");
            }
            catch (Exception fallbackEx)
            {
                _form.Log($"ERROR: Server failed to start on fallback address. Error: {fallbackEx.Message}");
            }
        }

        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { /* ignore */ }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener!.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException)       { break; }
            catch (Exception ex)
            {
                _form.Log($"Accept loop error: {ex.Message}");
                continue;
            }

            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
                _form.Log($"Rejected non-WebSocket request from {ctx.Request.RemoteEndPoint}");
                continue;
            }

            _ = HandleConnectionAsync(ctx, ct);
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext ctx, CancellationToken serverCt)
    {
        WebSocket? ws = null;
        string? assignedRole = null;
        var connCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        string clientIp = ctx.Request.RemoteEndPoint?.ToString() ?? "unknown";

        try
        {
            _form.Log($"Incoming WebSocket connection from {clientIp}");

            HttpListenerWebSocketContext wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
            ws = wsCtx.WebSocket;

            assignedRole = await DoHelloHandshakeAsync(ws, clientIp, connCts.Token);
            if (assignedRole == null)
            {
                _form.Log($"Hello handshake rejected/failed for {clientIp}");
                await CloseWebSocketAsync(ws, WebSocketCloseStatus.PolicyViolation, "role_taken or invalid hello");
                return;
            }

            SetStatus(assignedRole, "Connected");
            NotifyForm();
            _form.Log($"{assignedRole} connected from {clientIp}");

            var buffer = new byte[ReceiveBufferSize];
            var segment = new ArraySegment<byte>(buffer);

            while (ws.State == WebSocketState.Open && !connCts.Token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(segment, connCts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException ex)
                {
                    _form.Log($"WebSocket error on {assignedRole} ({clientIp}): {ex.Message}");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _form.Log($"{assignedRole} ({clientIp}) sent Close frame");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary && result.Count >= 1)
                {
                    byte lo = buffer[0];
                    byte hi = result.Count >= 2 ? buffer[1] : (byte)0;

                    BlastemFocuser.EnsureBlastemForeground();
                    KeyboardInput.ApplyButtonState(assignedRole, lo, hi);
                }
            }
        }
        catch (Exception ex)
        {
            _form.Log($"Error handling {assignedRole ?? "unknown"} ({clientIp}): {ex.Message}");
        }
        finally
        {
            connCts.Cancel();
            connCts.Dispose();

            if (assignedRole != null)
            {
                KeyboardInput.ReleaseAllKeys(assignedRole);
                ReleaseRole(assignedRole);
                SetStatus(assignedRole, "Not connected");
                NotifyForm();
                _form.Log($"{assignedRole} ({clientIp}) disconnected. All keys released.");
            }

            if (ws != null && ws.State == WebSocketState.Open)
            {
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* ignore */ }
            }
            ws?.Dispose();
        }
    }

    private async Task<string?> DoHelloHandshakeAsync(WebSocket ws, string clientIp, CancellationToken ct)
    {
        var buffer = new byte[512];
        var segment = new ArraySegment<byte>(buffer);

        WebSocketReceiveResult result;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            result = await ws.ReceiveAsync(segment, linked.Token);
        }
        catch (OperationCanceledException)
        {
            _form.Log($"Hello handshake timed out for {clientIp}");
            return null;
        }
        catch (WebSocketException ex)
        {
            _form.Log($"Hello handshake error for {clientIp}: {ex.Message}");
            return null;
        }

        if (result.MessageType != WebSocketMessageType.Text)
        {
            _form.Log($"Hello handshake error for {clientIp}: expected Text frame, got {result.MessageType}");
            return null;
        }

        string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        string? role = ParseHelloRole(json);

        if (role == null)
        {
            _form.Log($"Hello handshake error for {clientIp}: invalid JSON. Received: '{json}'");
            return null;
        }

        if (role != "player1" && role != "player2")
        {
            _form.Log($"Hello handshake error for {clientIp}: invalid role '{role}'");
            await SendTextAsync(ws, $"{{\"type\":\"hello_rejected\",\"reason\":\"invalid_role\",\"role\":\"{role}\"}}", ct);
            return null;
        }

        bool reserved = TryReserveRole(role);
        if (!reserved)
        {
            _form.Log($"Hello handshake error for {clientIp}: role '{role}' is already taken");
            await SendTextAsync(ws, $"{{\"type\":\"hello_rejected\",\"reason\":\"role_taken\",\"role\":\"{role}\"}}", ct);
            return null;
        }

        await SendTextAsync(ws, $"{{\"type\":\"hello_ack\",\"role\":\"{role}\"}}", ct);
        return role;
    }

    private static string? ParseHelloRole(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("type", out JsonElement typeEl)) return null;
            if (typeEl.GetString() != "hello") return null;
            if (!root.TryGetProperty("role", out JsonElement roleEl)) return null;
            return roleEl.GetString();
        }
        catch { return null; }
    }

    private bool TryReserveRole(string role)
    {
        lock (_rolesLock)
        {
            if (role == "player1") { if (_p1Role != null) return false; _p1Role = role; return true; }
            else                   { if (_p2Role != null) return false; _p2Role = role; return true; }
        }
    }

    private void ReleaseRole(string role)
    {
        lock (_rolesLock)
        {
            if (role == "player1") _p1Role = null;
            else _p2Role = null;
        }
    }

    private void SetStatus(string role, string status)
    {
        if (role == "player1") _p1Status = status;
        else _p2Status = status;
    }

    public void NotifyForm()
    {
        string blastemStatus = BlastemFocuser.IsFound ? "Found" : "Not detected";
        _form.UpdateStatus(_p1Status, _p2Status, blastemStatus);
    }

    private static async Task SendTextAsync(WebSocket ws, string text, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct); }
        catch { /* ignore */ }
    }

    private static async Task CloseWebSocketAsync(WebSocket ws, WebSocketCloseStatus status, string description)
    {
        if (ws.State == WebSocketState.Open)
        {
            try { await ws.CloseAsync(status, description, CancellationToken.None); }
            catch { /* ignore */ }
        }
    }
}
