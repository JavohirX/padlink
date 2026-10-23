package com.nescontroller

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import okhttp3.*
import okio.ByteString
import okio.ByteString.Companion.toByteString
import org.json.JSONObject
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

private const val TAG = "WebSocketManager"
private const val PREF_FILE = "nescontroller_prefs"
private const val PREF_LAST_URL = "last_ws_url"

object WebSocketManager {

    interface Listener {
        fun onConnected()
        fun onHelloAck(role: String)
        fun onHelloRejected(role: String, reason: String)
        fun onDisconnected()
        fun onReconnecting(attemptNumber: Int, lastError: String?)
    }

    private var listener: Listener? = null
    private var webSocket: WebSocket? = null
    private var client: OkHttpClient? = null
    private var prefs: SharedPreferences? = null

    private var currentUrl: String = ""
    private var currentRole: String = ""
    private var lastError: String? = null
    private val isConnected = AtomicBoolean(false)
    private val isReconnecting = AtomicBoolean(false)
    private val isStopped = AtomicBoolean(false)

    // Reconnect backoff delays in milliseconds
    private val backoffDelays = longArrayOf(0L, 1000L, 2000L, 5000L)
    private var reconnectAttempt = 0

    fun init(context: Context) {
        prefs = context.applicationContext.getSharedPreferences(PREF_FILE, Context.MODE_PRIVATE)
        client = OkHttpClient.Builder()
            .readTimeout(0, TimeUnit.MILLISECONDS) // No timeout for WebSocket
            .pingInterval(20, TimeUnit.SECONDS)
            .build()
    }

    fun setListener(l: Listener?) {
        listener = l
    }

    fun getCurrentUrl(): String = currentUrl

    fun getLastUrl(): String? {
        return prefs?.getString(PREF_LAST_URL, null)
            ?.takeIf { it.isNotBlank() }
    }

    fun connect(url: String) {
        currentUrl = url
        reconnectAttempt = 0
        isStopped.set(false)
        reconnectScheduled.set(false)
        doConnect()
    }

    private fun doConnect() {
        if (isStopped.get()) return

        val url = currentUrl
        if (url.isBlank()) return

        val c = client ?: return

        Log.d(TAG, "Connecting to $url (attempt $reconnectAttempt)")

        val request = Request.Builder().url(url).build()
        webSocket = c.newWebSocket(request, object : WebSocketListener() {

            override fun onOpen(ws: WebSocket, response: Response) {
                Log.d(TAG, "WebSocket opened")
                lastError = null
                isConnected.set(true)
                isReconnecting.set(false)
                reconnectScheduled.set(false)
                reconnectAttempt = 0
                listener?.onConnected()

                // If we have a role (reconnecting after initial hello_ack), re-send hello
                if (currentRole.isNotBlank()) {
                    sendHello(currentRole)
                }
            }

            override fun onMessage(ws: WebSocket, text: String) {
                Log.d(TAG, "Text message: $text")
                handleTextMessage(text)
            }

            override fun onMessage(ws: WebSocket, bytes: ByteString) {
                // We don't expect binary messages from server
                Log.d(TAG, "Binary message received (ignored)")
            }

            override fun onClosing(ws: WebSocket, code: Int, reason: String) {
                Log.d(TAG, "WebSocket closing: $code $reason")
                ws.close(1000, null)
            }

            override fun onClosed(ws: WebSocket, code: Int, reason: String) {
                Log.d(TAG, "WebSocket closed: $code $reason")
                handleDisconnect()
            }

            override fun onFailure(ws: WebSocket, t: Throwable, response: Response?) {
                Log.e(TAG, "WebSocket failure: ${t.message}")
                lastError = t.message
                handleDisconnect()
            }
        })
    }

    private fun handleTextMessage(text: String) {
        try {
            val json = JSONObject(text)
            when (json.optString("type")) {
                "hello_ack" -> {
                    val role = json.optString("role", currentRole)
                    // Cache the URL after successful hello_ack
                    prefs?.edit()?.putString(PREF_LAST_URL, currentUrl)?.apply()
                    listener?.onHelloAck(role)
                }
                "hello_rejected" -> {
                    val role = json.optString("role", currentRole)
                    val reason = json.optString("reason", "unknown")
                    listener?.onHelloRejected(role, reason)
                }
                else -> {
                    Log.d(TAG, "Unknown message type: ${json.optString("type")}")
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to parse message: ${e.message}")
        }
    }

    // Guard flag so onClosed + onFailure don't both trigger a reconnect loop
    private val reconnectScheduled = AtomicBoolean(false)

    private fun handleDisconnect() {
        val wasConnected = isConnected.getAndSet(false)

        if (isStopped.get()) {
            if (wasConnected) listener?.onDisconnected()
            return
        }

        if (wasConnected) {
            listener?.onDisconnected()
        }

        // Only schedule one reconnect attempt per disconnect event
        if (reconnectScheduled.compareAndSet(false, true)) {
            scheduleReconnect()
        }
    }

    private fun scheduleReconnect() {
        if (isStopped.get()) return
        if (currentUrl.isBlank()) return

        isReconnecting.set(true)
        reconnectAttempt++

        val delay = if (reconnectAttempt < backoffDelays.size) {
            backoffDelays[reconnectAttempt - 1]
        } else {
            5000L
        }

        val attempt = reconnectAttempt
        listener?.onReconnecting(attempt, lastError)
        Log.d(TAG, "Scheduling reconnect in ${delay}ms (attempt $attempt)")

        Thread {
            if (delay > 0) {
                Thread.sleep(delay)
            }
            // Reset flag so the next disconnect from this new connection attempt can schedule a reconnect
            reconnectScheduled.set(false)
            if (!isStopped.get()) {
                doConnect()
            }
        }.also { it.isDaemon = true }.start()
    }

    fun sendHello(role: String) {
        currentRole = role
        val json = JSONObject().apply {
            put("type", "hello")
            put("role", role)
        }
        val ws = webSocket
        if (ws != null && isConnected.get()) {
            ws.send(json.toString())
            Log.d(TAG, "Sent hello for role: $role")
        } else {
            Log.w(TAG, "Cannot send hello — not connected")
        }
    }

    fun sendButtonState(bitmask: Byte) {
        val ws = webSocket
        if (ws != null && isConnected.get()) {
            val bytes = byteArrayOf(bitmask).toByteString()
            ws.send(bytes)
        }
    }

    fun disconnect() {
        isStopped.set(true)
        isConnected.set(false)
        isReconnecting.set(false)
        reconnectScheduled.set(false)
        currentRole = ""
        webSocket?.close(1000, "User disconnected")
        webSocket = null
    }

    fun isCurrentlyConnected(): Boolean = isConnected.get()
}
