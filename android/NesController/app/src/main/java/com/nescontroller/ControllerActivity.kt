package com.nescontroller

import android.content.Context
import android.content.SharedPreferences
import android.media.AudioAttributes
import android.os.Build
import android.os.Bundle
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.view.HapticFeedbackConstants
import android.view.View
import android.view.WindowManager
import androidx.appcompat.app.AppCompatActivity
import com.nescontroller.databinding.ActivityControllerBinding

class ControllerActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_ROLE = "extra_role"
        private const val PREF_FILE = "nescontroller_prefs"
        private const val PREF_HAPTIC = "haptic_enabled"

        // NES button bitmask constants
        const val BIT_UP: Int = 0x01
        const val BIT_DOWN: Int = 0x02
        const val BIT_LEFT: Int = 0x04
        const val BIT_RIGHT: Int = 0x08
        const val BIT_A: Int = 0x10
        const val BIT_B: Int = 0x20
        const val BIT_SELECT: Int = 0x40
        const val BIT_START: Int = 0x80
    }

    private lateinit var binding: ActivityControllerBinding
    private lateinit var prefs: SharedPreferences
    private var vibrator: Vibrator? = null

    private var hapticEnabled = true

    // Tracks bits contributed by each view: viewId -> bitmask
    private val viewBits = mutableMapOf<Int, Int>()

    // Turbo button state tracking
    private var turboAActive = false
    private var turboBActive = false
    private var turboAToggle = false
    private var turboBToggle = false

    private val handler = android.os.Handler(android.os.Looper.getMainLooper())

    private val turboRunnable = object : Runnable {
        override fun run() {
            var changed = false
            if (turboAActive) {
                turboAToggle = !turboAToggle
                changed = true
            } else {
                turboAToggle = false
            }

            if (turboBActive) {
                turboBToggle = !turboBToggle
                changed = true
            } else {
                turboBToggle = false
            }

            if (changed) {
                updateControllerState()
                handler.postDelayed(this, 50L) // 50ms rapid fire intervals
            }
        }
    }

    private val wsListener = object : WebSocketManager.Listener {
        override fun onConnected() {
            runOnUiThread {
                binding.bannerReconnecting.visibility = View.GONE
            }
        }

        override fun onHelloAck(role: String) {
            runOnUiThread {
                binding.bannerReconnecting.visibility = View.GONE
            }
        }

        override fun onHelloRejected(role: String, reason: String) {
            // Role was rejected on reconnect — shouldn't normally happen
            // since same role is re-sent; show reconnecting banner
            runOnUiThread {
                binding.bannerReconnecting.visibility = View.VISIBLE
                binding.tvReconnecting.text = getString(R.string.role_rejected_reconnect)
            }
        }

        override fun onDisconnected() {
            runOnUiThread {
                binding.bannerReconnecting.visibility = View.VISIBLE
                binding.tvReconnecting.text = getString(R.string.reconnecting)
            }
        }

        override fun onReconnecting(attemptNumber: Int, lastError: String?) {
            runOnUiThread {
                binding.bannerReconnecting.visibility = View.VISIBLE
                binding.tvReconnecting.text = getString(R.string.reconnecting_attempt, attemptNumber)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Keep screen on
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        // Full-screen immersive
        @Suppress("DEPRECATION")
        window.decorView.systemUiVisibility = (
            View.SYSTEM_UI_FLAG_FULLSCREEN or
            View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
            View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
            View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
            View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
            View.SYSTEM_UI_FLAG_LAYOUT_STABLE
        )

        binding = ActivityControllerBinding.inflate(layoutInflater)
        setContentView(binding.root)

        prefs = getSharedPreferences(PREF_FILE, Context.MODE_PRIVATE)
        hapticEnabled = prefs.getBoolean(PREF_HAPTIC, true)
        updateHapticIcon()

        vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val vm = getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager
            vm.defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
        }

        val role = intent.getStringExtra(EXTRA_ROLE) ?: "player1"
        // "player1" -> "Player 1", "player2" -> "Player 2"
        binding.tvRole.text = role.replace(Regex("player(\\d+)", RegexOption.IGNORE_CASE)) { mr ->
            "Player ${mr.groupValues[1]}"
        }

        WebSocketManager.setListener(wsListener)

        setupButtons()

        binding.btnSettings.setOnClickListener {
            hapticEnabled = !hapticEnabled
            prefs.edit().putBoolean(PREF_HAPTIC, hapticEnabled).apply()
            updateHapticIcon()
            if (hapticEnabled) {
                triggerHaptic()
            }
        }
    }

    private fun updateHapticIcon() {
        binding.btnSettings.text = if (hapticEnabled) "V" else "M" // V = vibrate, M = mute
        binding.btnSettings.alpha = if (hapticEnabled) 1.0f else 0.4f
    }

    private fun setupButtons() {
        // D-Pad
        binding.dpadView.onBitsChanged = { bits ->
            onViewBitsChanged(binding.dpadView.id, bits)
        }

        // A button
        binding.btnA.bitValue = BIT_A
        binding.btnA.label = getString(R.string.btn_label_a)
        binding.btnA.onStateChanged = { bits ->
            onViewBitsChanged(binding.btnA.id, bits)
        }

        // B button
        binding.btnB.bitValue = BIT_B
        binding.btnB.label = getString(R.string.btn_label_b)
        binding.btnB.onStateChanged = { bits ->
            onViewBitsChanged(binding.btnB.id, bits)
        }

        // Turbo A button
        binding.btnTA.bitValue = BIT_A
        binding.btnTA.label = getString(R.string.btn_label_ta)
        binding.btnTA.onStateChanged = { bits ->
            onTurboStateChanged(BIT_A, bits != 0)
        }

        // Turbo B button
        binding.btnTB.bitValue = BIT_B
        binding.btnTB.label = getString(R.string.btn_label_tb)
        binding.btnTB.onStateChanged = { bits ->
            onTurboStateChanged(BIT_B, bits != 0)
        }

        // Select button
        binding.btnSelect.bitValue = BIT_SELECT
        binding.btnSelect.label = getString(R.string.btn_label_select)
        binding.btnSelect.onStateChanged = { bits ->
            onViewBitsChanged(binding.btnSelect.id, bits)
        }

        // Start button
        binding.btnStart.bitValue = BIT_START
        binding.btnStart.label = getString(R.string.btn_label_start)
        binding.btnStart.onStateChanged = { bits ->
            onViewBitsChanged(binding.btnStart.id, bits)
        }
    }

    private fun onViewBitsChanged(viewId: Int, bits: Int) {
        val oldBits = viewBits[viewId] ?: 0
        val newPresses = bits and oldBits.inv()
        val shouldHaptic = newPresses != 0

        viewBits[viewId] = bits
        val bitmask = computeBitmask()
        sendBitmask(bitmask, shouldHaptic)
    }

    private fun onTurboStateChanged(bit: Int, pressed: Boolean) {
        if (bit == BIT_A) {
            if (turboAActive != pressed) {
                turboAActive = pressed
                turboAToggle = pressed
                if (pressed) {
                    if (hapticEnabled) triggerHaptic()
                    handler.removeCallbacks(turboRunnable)
                    handler.post(turboRunnable)
                }
                updateControllerState()
            }
        } else if (bit == BIT_B) {
            if (turboBActive != pressed) {
                turboBActive = pressed
                turboBToggle = pressed
                if (pressed) {
                    if (hapticEnabled) triggerHaptic()
                    handler.removeCallbacks(turboRunnable)
                    handler.post(turboRunnable)
                }
                updateControllerState()
            }
        }
    }

    private fun updateControllerState() {
        val bitmask = computeBitmask()
        sendBitmask(bitmask, false)
    }

    private fun computeBitmask(): Int {
        var mask = 0
        for (bits in viewBits.values) {
            mask = mask or bits
        }
        if (turboAActive && turboAToggle) {
            mask = mask or BIT_A
        }
        if (turboBActive && turboBToggle) {
            mask = mask or BIT_B
        }
        return mask
    }

    private fun sendBitmask(bitmask: Int, triggerHaptic: Boolean) {
        if (triggerHaptic && hapticEnabled) {
            triggerHaptic()
        }
        WebSocketManager.sendButtonState(bitmask.toByte())
    }

    private fun triggerHaptic() {
        if (!hapticEnabled) return

        // Prefer performHapticFeedback for standard UI interactions as it's more reliable across devices
        val performed = binding.root.performHapticFeedback(
            HapticFeedbackConstants.VIRTUAL_KEY,
            HapticFeedbackConstants.FLAG_IGNORE_GLOBAL_SETTING
        )

        // Fallback to Vibrator if performHapticFeedback is not handled
        if (!performed) {
            val v = vibrator ?: return
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                val effect = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    VibrationEffect.createPredefined(VibrationEffect.EFFECT_CLICK)
                } else {
                    VibrationEffect.createOneShot(30, VibrationEffect.DEFAULT_AMPLITUDE)
                }

                val attrs = AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_ASSISTANCE_SONIFICATION)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                    .build()

                v.vibrate(effect, attrs)
            } else {
                @Suppress("DEPRECATION")
                v.vibrate(30)
            }
        }
    }

    override fun onResume() {
        super.onResume()
        WebSocketManager.setListener(wsListener)
        // Update reconnecting banner based on current state
        if (!WebSocketManager.isCurrentlyConnected()) {
            binding.bannerReconnecting.visibility = View.VISIBLE
            binding.tvReconnecting.text = getString(R.string.reconnecting)
        } else {
            binding.bannerReconnecting.visibility = View.GONE
        }
    }

    override fun onPause() {
        super.onPause()
        handler.removeCallbacks(turboRunnable)
        turboAActive = false
        turboBActive = false
        turboAToggle = false
        turboBToggle = false
        // Release all buttons immediately
        WebSocketManager.sendButtonState(0x00)
        viewBits.clear()
    }

    override fun onDestroy() {
        super.onDestroy()
        WebSocketManager.setListener(null)
    }
}
