package com.segacontroller

import android.content.Intent
import android.os.Bundle
import android.view.View
import androidx.appcompat.app.AppCompatActivity
import com.segacontroller.databinding.ActivityRolePickerBinding

class RolePickerActivity : AppCompatActivity() {

    private lateinit var binding: ActivityRolePickerBinding
    private var pendingRole: String? = null

    private val wsListener = object : WebSocketManager.Listener {
        override fun onConnected() {
            runOnUiThread {
                val role = pendingRole
                if (role != null) { showConnecting(); WebSocketManager.sendHello(role) }
                else showIdle()
            }
        }

        override fun onHelloAck(role: String) {
            runOnUiThread { navigateToController(role) }
        }

        override fun onHelloRejected(role: String, reason: String) {
            runOnUiThread { pendingRole = null; showError(role, reason) }
        }

        override fun onDisconnected() {
            runOnUiThread { if (pendingRole != null) showError(null, "disconnected") }
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
            binding.tvConnectionUrl.visibility = View.VISIBLE
        }

        binding.btnPlayer1.setOnClickListener { selectRole("player1") }
        binding.btnPlayer2.setOnClickListener { selectRole("player2") }
        showIdle()
    }

    override fun onResume() {
        super.onResume()
        WebSocketManager.setListener(wsListener)
        if (pendingRole != null && !WebSocketManager.isCurrentlyConnected()) {
            binding.tvStatus.text = getString(R.string.connecting)
            binding.tvStatus.visibility = View.VISIBLE
        }
    }

    override fun onDestroy() {
        super.onDestroy()
    }

    private fun selectRole(role: String) {
        pendingRole = role
        showConnecting()
        if (WebSocketManager.isCurrentlyConnected()) WebSocketManager.sendHello(role)
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
            reason == "disconnected"    -> getString(R.string.connection_lost)
            rejectedRole == "player1"   -> getString(R.string.role_taken_player1)
            rejectedRole == "player2"   -> getString(R.string.role_taken_player2)
            else                        -> getString(R.string.role_rejected, reason)
        }
        binding.tvStatus.text = message
        binding.tvStatus.visibility = View.VISIBLE
        binding.tvStatus.setTextColor(getColor(R.color.status_error))
        binding.btnPlayer1.isEnabled = true
        binding.btnPlayer2.isEnabled = true
    }

    private fun navigateToController(role: String) {
        pendingRole = null
        WebSocketManager.setListener(null)
        val intent = Intent(this, ControllerActivity::class.java).apply {
            putExtra(ControllerActivity.EXTRA_ROLE, role)
        }
        startActivity(intent)
    }
}
