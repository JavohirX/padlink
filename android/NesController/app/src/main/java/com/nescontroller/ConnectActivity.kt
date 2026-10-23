package com.nescontroller

import android.content.Intent
import android.os.Bundle
import android.view.View
import androidx.appcompat.app.AppCompatActivity
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanIntentResult
import com.journeyapps.barcodescanner.ScanOptions
import com.nescontroller.databinding.ActivityConnectBinding

class ConnectActivity : AppCompatActivity() {

    private lateinit var binding: ActivityConnectBinding

    private val barcodeLauncher = registerForActivityResult(ScanContract()) { result: ScanIntentResult ->
        val scannedUrl = result.contents
        if (!scannedUrl.isNullOrBlank()) {
            onUrlObtained(scannedUrl)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityConnectBinding.inflate(layoutInflater)
        setContentView(binding.root)

        WebSocketManager.init(this)

        val lastUrl = WebSocketManager.getLastUrl()
        if (lastUrl != null) {
            binding.btnReconnect.visibility = View.VISIBLE
            binding.btnReconnect.text = getString(R.string.reconnect_to, lastUrl)
            binding.btnReconnect.setOnClickListener {
                onUrlObtained(lastUrl)
            }
        } else {
            binding.btnReconnect.visibility = View.GONE
        }

        binding.btnScanQr.setOnClickListener {
            launchQrScanner()
        }
    }

    private fun launchQrScanner() {
        val options = ScanOptions().apply {
            setPrompt(getString(R.string.scan_prompt))
            setBeepEnabled(false)
            setOrientationLocked(false)
            setBarcodeImageEnabled(false)
        }
        barcodeLauncher.launch(options)
    }

    private fun onUrlObtained(url: String) {
        WebSocketManager.connect(url)
        val intent = Intent(this, RolePickerActivity::class.java)
        startActivity(intent)
    }
}
