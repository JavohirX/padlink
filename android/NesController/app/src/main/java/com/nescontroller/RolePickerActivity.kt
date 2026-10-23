package com.nescontroller

import android.content.Intent
import android.os.Bundle
import android.view.View
import androidx.appcompat.app.AppCompatActivity
import com.nescontroller.databinding.ActivityRolePickerBinding

class RolePickerActivity : AppCompatActivity() {

    private lateinit var binding: ActivityRolePickerBinding
    private var pendingRole: String? = null

    private val wsListener = object : WebSocketManager.Listener {
        override fun onConnected() {
            runOnUiThread {
                // Connection (re)established — if we had a pending role selection, re-send
                val role = pendingRole
                if (role != null) {
                    showConnecting()
                    WebSocketManager.sendHello(role)
                } else {
                    showIdle()
                }
            }
        }

        override fun onHelloAck(role: String) {
            runOnUiThread {
                navigateToController(role)
            }
        }

        override fun onHelloRejected(role: String, reason: String) {
            runOnUiThread {
                pendingRole = null
                showError(role, reason)
            }
        }

        override fun onDisconnected() {
            runOnUiThread {
                if (pendingRole != null) {
                    showError(null, "disconnected")
                }
            }
        }

        override fun onReconnecting(attemptNumber: Int, lastError: String?) {
            runOnUiThread {
                if (pendingRole != null) {
                    val base = getString(R.string.reconnecting_attempt, attemptNumber)
                    binding.tvStatus.text = if (lastError != null) "$base\n$lastError" else base
                    binding.tvStatus.visibility = View.VISIBLE
                }
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityRolePickerBinding.inflate(layoutInflater)
        setContentView(binding.root)

        WebSocketManager.setListener(wsListener)

        val url = WebSocketManager.getCurrentUrl()
        if (url.isNotBlank()) {
            binding.tvConnectionUrl.text = url
            binding.tvConnectionUrl.visibility = android.view.View.VISIBLE
        }

        binding.btnPlayer1.setOnClickListener {
            selectRole("player1")
        }

        binding.btnPlayer2.setOnClickListener {
            selectRole("player2")
        }

        showIdle()
    }

    override fun onResume() {
        super.onResume()
        WebSocketManager.setListener(wsListener)
        // Only show connecting status if we have an active pending role and aren't yet connected
        if (pendingRole != null && !WebSocketManager.isCurrentlyConnected()) {
            binding.tvStatus.text = getString(R.string.connecting)
            binding.tvStatus.visibility = View.VISIBLE
        }
    }

    override fun onPause() {
        super.onPause()
        // Only clear listener if we're not navigating to ControllerActivity
    }

    override fun onDestroy() {
        super.onDestroy()
        if (!isFinishing || pendingRole != null) {
            // If backing out, clear listener but don't disconnect
            // (auto-reconnect keeps running in background until ControllerActivity
            // or explicit disconnect)
        }
    }

    private fun selectRole(role: String) {
        pendingRole = role
        showConnecting()
        if (WebSocketManager.isCurrentlyConnected()) {
            WebSocketManager.sendHello(role)
        }
        // If not connected yet, wsListener.onConnected will fire and send hello
    }

    private fun showIdle() {
        binding.tvStatus.visibility = View.GONE
        binding.btnPlayer1.isEnabled = true
        binding.btnPlayer2.isEnabled = true
    }

    private fun showConnecting() {
        binding.tvStatus.text = getString(R.string.connecting)
        binding.tvStatus.visibility = View.VISIBLE
        binding.tvStatus.setTextColor(getColor(R.color.status_info))
        binding.btnPlayer1.isEnabled = false
        binding.btnPlayer2.isEnabled = false
    }

    private fun showError(rejectedRole: String?, reason: String) {
        val message = when {
            reason == "disconnected" -> getString(R.string.connection_lost)
            rejectedRole == "player1" -> getString(R.string.role_taken_player1)
            rejectedRole == "player2" -> getString(R.string.role_taken_player2)
            else -> getString(R.string.role_rejected, reason)
        }
        binding.tvStatus.text = message
        binding.tvStatus.visibility = View.VISIBLE
        binding.tvStatus.setTextColor(getColor(R.color.status_error))
        binding.btnPlayer1.isEnabled = true
        binding.btnPlayer2.isEnabled = true
    }

    private fun navigateToController(role: String) {
        pendingRole = null
        // Clear listener before navigating — ControllerActivity will set its own
        WebSocketManager.setListener(null)
        val intent = Intent(this, ControllerActivity::class.java).apply {
            putExtra(ControllerActivity.EXTRA_ROLE, role)
        }
        startActivity(intent)
        // Don't finish — user can back-navigate to pick a different role
    }
}
