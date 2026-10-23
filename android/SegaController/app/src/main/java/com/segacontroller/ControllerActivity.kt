package com.segacontroller

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
import com.segacontroller.databinding.ActivityControllerBinding

class ControllerActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_ROLE = "extra_role"
        private const val PREF_FILE = "segacontroller_prefs"
        private const val PREF_HAPTIC = "haptic_enabled"

        // Sega Genesis 6-button bitmask (Byte 0 = low, Byte 1 = high)
        // Byte 0
        const val BIT_UP:    Int = 0x0001
        const val BIT_DOWN:  Int = 0x0002
        const val BIT_LEFT:  Int = 0x0004
        const val BIT_RIGHT: Int = 0x0008
        const val BIT_A:     Int = 0x0010
        const val BIT_B:     Int = 0x0020
        const val BIT_C:     Int = 0x0040
        const val BIT_START: Int = 0x0080
        // Byte 1
        const val BIT_X:    Int = 0x0100
        const val BIT_Y:    Int = 0x0200
        const val BIT_Z:    Int = 0x0400
        const val BIT_MODE: Int = 0x0800
    }

    private lateinit var binding: ActivityControllerBinding
    private lateinit var prefs: SharedPreferences
    private var vibrator: Vibrator? = null
    private var hapticEnabled = true

    // Bits contributed by each view
    private val viewBits = mutableMapOf<Int, Int>()

    // Turbo state for A and C (common Sega turbo buttons)
    private var turboAActive = false
    private var turboCActive = false
    private var turboAToggle = false
    private var turboCToggle = false

    private val handler = android.os.Handler(android.os.Looper.getMainLooper())

    private val turboRunnable = object : Runnable {
        override fun run() {
            var changed = false
            if (turboAActive) { turboAToggle = !turboAToggle; changed = true }
            else turboAToggle = false
            if (turboCActive) { turboCToggle = !turboCToggle; changed = true }
            else turboCToggle = false

            if (changed) {
                updateControllerState()
                handler.postDelayed(this, 50L)
            }
        }
    }

    private val wsListener = object : WebSocketManager.Listener {
        override fun onConnected() {
            runOnUiThread { binding.bannerReconnecting.visibility = View.GONE }
        }
        override fun onHelloAck(role: String) {
            runOnUiThread { binding.bannerReconnecting.visibility = View.GONE }
        }
        override fun onHelloRejected(role: String, reason: String) {
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
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

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
        binding.tvRole.text = role.replace(Regex("player(\\d+)", RegexOption.IGNORE_CASE)) { mr ->
            "Player ${mr.groupValues[1]}"
        }

        WebSocketManager.setListener(wsListener)
        setupButtons()

        binding.btnSettings.setOnClickListener {
            hapticEnabled = !hapticEnabled
            prefs.edit().putBoolean(PREF_HAPTIC, hapticEnabled).apply()
            updateHapticIcon()
            if (hapticEnabled) triggerHaptic()
        }
    }

    private fun updateHapticIcon() {
        binding.btnSettings.text = if (hapticEnabled) "V" else "M"
        binding.btnSettings.alpha = if (hapticEnabled) 1.0f else 0.4f
    }

    private fun setupButtons() {
        // D-Pad
        binding.dpadView.onBitsChanged = { bits -> onViewBitsChanged(binding.dpadView.id, bits) }

        // A button
        binding.btnA.bitValue = BIT_A
        binding.btnA.label = getString(R.string.btn_label_a)
        binding.btnA.onStateChanged = { bits -> onViewBitsChanged(binding.btnA.id, bits) }

        // B button
        binding.btnB.bitValue = BIT_B
        binding.btnB.label = getString(R.string.btn_label_b)
        binding.btnB.onStateChanged = { bits -> onViewBitsChanged(binding.btnB.id, bits) }

        // C button
        binding.btnC.bitValue = BIT_C
        binding.btnC.label = getString(R.string.btn_label_c)
        binding.btnC.onStateChanged = { bits -> onViewBitsChanged(binding.btnC.id, bits) }

        // X button
        binding.btnX.bitValue = BIT_X
        binding.btnX.label = getString(R.string.btn_label_x)
        binding.btnX.onStateChanged = { bits -> onViewBitsChanged(binding.btnX.id, bits) }

        // Y button
        binding.btnY.bitValue = BIT_Y
        binding.btnY.label = getString(R.string.btn_label_y)
        binding.btnY.onStateChanged = { bits -> onViewBitsChanged(binding.btnY.id, bits) }

        // Z button
        binding.btnZ.bitValue = BIT_Z
        binding.btnZ.label = getString(R.string.btn_label_z)
        binding.btnZ.onStateChanged = { bits -> onViewBitsChanged(binding.btnZ.id, bits) }

        // Turbo A
        binding.btnTA.bitValue = BIT_A
        binding.btnTA.label = getString(R.string.btn_label_ta)
        binding.btnTA.onStateChanged = { bits -> onTurboStateChanged(BIT_A, bits != 0) }

        // Turbo C
        binding.btnTC.bitValue = BIT_C
        binding.btnTC.label = getString(R.string.btn_label_tc)
        binding.btnTC.onStateChanged = { bits -> onTurboStateChanged(BIT_C, bits != 0) }

        // Start
        binding.btnStart.bitValue = BIT_START
        binding.btnStart.label = getString(R.string.btn_label_start)
        binding.btnStart.onStateChanged = { bits -> onViewBitsChanged(binding.btnStart.id, bits) }

        // Mode
        binding.btnMode.bitValue = BIT_MODE
        binding.btnMode.label = getString(R.string.btn_label_mode)
        binding.btnMode.onStateChanged = { bits -> onViewBitsChanged(binding.btnMode.id, bits) }
    }

    private fun onViewBitsChanged(viewId: Int, bits: Int) {
        val oldBits = viewBits[viewId] ?: 0
        val newPresses = bits and oldBits.inv()
        val shouldHaptic = newPresses != 0
        viewBits[viewId] = bits
        sendBitmask(computeBitmask(), shouldHaptic)
    }

    private fun onTurboStateChanged(bit: Int, pressed: Boolean) {
        if (bit == BIT_A) {
            if (turboAActive != pressed) {
                turboAActive = pressed; turboAToggle = pressed
                if (pressed) { if (hapticEnabled) triggerHaptic(); handler.removeCallbacks(turboRunnable); handler.post(turboRunnable) }
                updateControllerState()
            }
        } else if (bit == BIT_C) {
            if (turboCActive != pressed) {
                turboCActive = pressed; turboCToggle = pressed
                if (pressed) { if (hapticEnabled) triggerHaptic(); handler.removeCallbacks(turboRunnable); handler.post(turboRunnable) }
                updateControllerState()
            }
        }
    }

    private fun updateControllerState() { sendBitmask(computeBitmask(), false) }

    private fun computeBitmask(): Int {
        var mask = 0
        for (bits in viewBits.values) mask = mask or bits
        if (turboAActive && turboAToggle) mask = mask or BIT_A
        if (turboCActive && turboCToggle) mask = mask or BIT_C
        return mask
    }

    private fun sendBitmask(bitmask: Int, triggerHaptic: Boolean) {
        if (triggerHaptic && hapticEnabled) triggerHaptic()
        WebSocketManager.sendButtonState(bitmask.toShort())
    }

    private fun triggerHaptic() {
        if (!hapticEnabled) return
        val performed = binding.root.performHapticFeedback(
            HapticFeedbackConstants.VIRTUAL_KEY,
            HapticFeedbackConstants.FLAG_IGNORE_GLOBAL_SETTING
        )
        if (!performed) {
            val v = vibrator ?: return
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                val effect = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q)
                    VibrationEffect.createPredefined(VibrationEffect.EFFECT_CLICK)
                else
                    VibrationEffect.createOneShot(30, VibrationEffect.DEFAULT_AMPLITUDE)
                val attrs = AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_ASSISTANCE_SONIFICATION)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                    .build()
                v.vibrate(effect, attrs)
            } else {
                @Suppress("DEPRECATION") v.vibrate(30)
            }
        }
    }

    override fun onResume() {
        super.onResume()
        WebSocketManager.setListener(wsListener)
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
        turboAActive = false; turboCActive = false
        turboAToggle = false; turboCToggle = false
        WebSocketManager.sendButtonState(0)
        viewBits.clear()
    }

    override fun onDestroy() {
        super.onDestroy()
        WebSocketManager.setListener(null)
    }
}
