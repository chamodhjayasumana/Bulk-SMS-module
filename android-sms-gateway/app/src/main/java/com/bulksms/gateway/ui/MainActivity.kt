package com.bulksms.gateway.ui

import android.Manifest
import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.bulksms.gateway.SmsGatewayApp
import com.bulksms.gateway.databinding.ActivityMainBinding
import com.bulksms.gateway.server.GatewayHttpServer
import com.bulksms.gateway.service.GatewayForegroundService
import com.bulksms.gateway.sms.SmsSender
import com.bulksms.gateway.util.NetworkUtils
import java.util.UUID

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private val app get() = application as SmsGatewayApp
    private val port = GatewayHttpServer.DEFAULT_PORT

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { result ->
        val smsGranted = result[Manifest.permission.SEND_SMS] == true
        if (!smsGranted) {
            toast("SEND_SMS permission is required to send messages")
        }
        refreshUi()
    }

    private val stateReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            refreshUi()
            intent?.getStringExtra(GatewayForegroundService.EXTRA_ERROR)?.let {
                if (it.isNotBlank()) toast(it)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // Ensure token exists on first launch
        binding.txtToken.text = app.tokenStore.getOrCreateToken()

        binding.btnStart.setOnClickListener { startGateway() }
        binding.btnStop.setOnClickListener { stopGateway() }
        binding.btnTestSms.setOnClickListener { sendTestSms() }
        binding.btnCopyToken.setOnClickListener { copyToken() }
        binding.btnRotateToken.setOnClickListener { rotateToken() }

        requestNeededPermissions()
        refreshUi()
    }

    override fun onStart() {
        super.onStart()
        val filter = IntentFilter().apply {
            addAction(GatewayForegroundService.ACTION_STATE_CHANGED)
            addAction(GatewayForegroundService.ACTION_STATS_CHANGED)
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(stateReceiver, filter, RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("UnspecifiedRegisterReceiverFlag")
            registerReceiver(stateReceiver, filter)
        }
        refreshUi()
    }

    override fun onStop() {
        unregisterReceiver(stateReceiver)
        super.onStop()
    }

    private fun requestNeededPermissions() {
        val needed = mutableListOf<String>()
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.SEND_SMS)
            != PackageManager.PERMISSION_GRANTED
        ) {
            needed += Manifest.permission.SEND_SMS
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
            != PackageManager.PERMISSION_GRANTED
        ) {
            needed += Manifest.permission.POST_NOTIFICATIONS
        }
        if (needed.isNotEmpty()) {
            permissionLauncher.launch(needed.toTypedArray())
        }
    }

    private fun startGateway() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.SEND_SMS)
            != PackageManager.PERMISSION_GRANTED
        ) {
            requestNeededPermissions()
            toast("Grant SMS permission first")
            return
        }
        GatewayForegroundService.start(this, port)
        toast("Starting gateway…")
        binding.root.postDelayed({ refreshUi() }, 500)
    }

    private fun stopGateway() {
        GatewayForegroundService.stop(this)
        toast("Stopping gateway…")
        binding.root.postDelayed({ refreshUi() }, 400)
    }

    private fun sendTestSms() {
        val phone = binding.inputPhone.text?.toString()?.trim().orEmpty()
        val message = binding.inputMessage.text?.toString()?.trim().orEmpty()
        if (phone.isBlank() || message.isBlank()) {
            toast("Enter phone and message")
            return
        }

        Thread {
            val normalized = normalizeForDisplay(phone)
            val outcome = SmsSender(this).send(normalized, message)
            runOnUiThread {
                if (outcome.success) {
                    app.stats.messagesSent.incrementAndGet()
                    app.stats.successful.incrementAndGet()
                    app.stats.addRecent(
                        com.bulksms.gateway.data.RecentMessage(
                            normalized,
                            "SENT",
                            "TEST-${UUID.randomUUID().toString().take(8)}"
                        )
                    )
                    toast("Test SMS sent")
                } else {
                    app.stats.messagesSent.incrementAndGet()
                    app.stats.failed.incrementAndGet()
                    app.stats.addRecent(
                        com.bulksms.gateway.data.RecentMessage(
                            normalized,
                            "FAILED",
                            "TEST-${UUID.randomUUID().toString().take(8)}",
                            outcome.error
                        )
                    )
                    toast(outcome.error ?: "Test SMS failed")
                }
                refreshUi()
            }
        }.start()
    }

    private fun normalizeForDisplay(raw: String): String {
        val digits = raw.filter { it.isDigit() }
        return when {
            digits.length == 10 && digits.startsWith("0") -> "94" + digits.substring(1)
            digits.startsWith("94") -> digits
            else -> digits
        }
    }

    private fun copyToken() {
        val token = app.tokenStore.getToken()
        val clipboard = getSystemService(CLIPBOARD_SERVICE) as ClipboardManager
        clipboard.setPrimaryClip(ClipData.newPlainText("gateway_token", token))
        toast("Token copied")
    }

    private fun rotateToken() {
        val token = app.tokenStore.rotateToken()
        binding.txtToken.text = token
        toast("Token rotated — update ASP.NET config")
    }

    private fun refreshUi() {
        val online = GatewayForegroundService.isRunning() || app.stats.online.get()
        val ip = NetworkUtils.getWifiIpLegacy(this)
            ?: NetworkUtils.getLocalIpAddress()
        val sim = NetworkUtils.getSimOperatorName(this)
        val network = when {
            NetworkUtils.isWifiConnected(this) -> "Connected (Wi-Fi)"
            NetworkUtils.isNetworkAvailable(this) -> "Connected"
            else -> "Disconnected"
        }

        binding.txtStatus.text = if (online) "Gateway Status: ONLINE" else "Gateway Status: OFFLINE"
        binding.txtStatus.setTextColor(
            ContextCompat.getColor(
                this,
                if (online) com.bulksms.gateway.R.color.ok else com.bulksms.gateway.R.color.fail
            )
        )
        binding.txtSim.text = "SIM: $sim"
        binding.txtNetwork.text = "Network: $network"
        binding.txtIp.text = "Phone IP: $ip"
        binding.txtPort.text = "Port: $port"
        binding.txtCounts.text =
            "Messages Sent: ${app.stats.messagesSent.get()}\n" +
                "Successful: ${app.stats.successful.get()}\n" +
                "Failed: ${app.stats.failed.get()}"
        binding.txtToken.text = app.tokenStore.getToken()

        val recent = app.stats.recent
        binding.txtRecent.text = if (recent.isEmpty()) {
            "No messages yet"
        } else {
            recent.take(20).joinToString("\n") { item ->
                val err = item.error?.let { " ($it)" } ?: ""
                "${item.phoneNumber}  ${item.status}$err"
            }
        }

        binding.btnStart.isEnabled = !online
        binding.btnStop.isEnabled = online
    }

    private fun toast(msg: String) {
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
    }
}
