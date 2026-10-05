package com.bulksms.gateway.ui

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.OpenableColumns
import android.view.WindowManager
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import com.bulksms.gateway.SmsGatewayApp
import com.bulksms.gateway.bulk.CsvBulkSendService
import com.bulksms.gateway.bulk.CsvRecipientParser
import com.bulksms.gateway.bulk.MobileNumberValidator
import com.bulksms.gateway.databinding.ActivityBulkSendBinding
import com.bulksms.gateway.service.GatewayForegroundService
import java.nio.charset.Charset

class BulkSendActivity : AppCompatActivity() {

    companion object {
        private const val MAX_FILE_BYTES = 5_000_000
    }

    private lateinit var binding: ActivityBulkSendBinding
    private val app get() = application as SmsGatewayApp

    private val pickCsv = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) readCsv(uri)
    }

    private val progressReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            render()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityBulkSendBinding.inflate(layoutInflater)
        setContentView(binding.root)

        binding.btnPickCsv.setOnClickListener {
            if (app.bulkSession.running.get()) {
                toast("Wait until the current send finishes")
                return@setOnClickListener
            }
            pickCsv.launch(
                arrayOf(
                    "text/csv",
                    "text/comma-separated-values",
                    "text/plain",
                    "application/csv",
                    "application/vnd.ms-excel",
                    "application/octet-stream",
                    "*/*"
                )
            )
        }
        binding.btnSend.setOnClickListener { confirmAndSend() }
        binding.btnStop.setOnClickListener {
            app.bulkSession.cancel.set(true)
            CsvBulkSendService.stop(this)
            toast("Stopping after the current SMS")
        }
        binding.inputMessage.setText(app.bulkSession.message)
        binding.inputMessage.addTextChangedListener(SimpleTextWatcher { renderMessageMeta() })
        render()
    }

    override fun onStart() {
        super.onStart()
        val filter = IntentFilter(CsvBulkSendService.ACTION_PROGRESS)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(progressReceiver, filter, RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("UnspecifiedRegisterReceiverFlag")
            registerReceiver(progressReceiver, filter)
        }
        render()
    }

    override fun onStop() {
        unregisterReceiver(progressReceiver)
        super.onStop()
    }

    private fun readCsv(uri: Uri) {
        val name = displayName(uri)
        if (name.endsWith(".xlsx", ignoreCase = true) || name.endsWith(".xls", ignoreCase = true)) {
            toast("Save the sheet as CSV, then choose that file")
            return
        }

        Thread {
            val text = try {
                contentResolver.openInputStream(uri)?.use { input ->
                    readLimited(input, MAX_FILE_BYTES)
                }
            } catch (ex: Exception) {
                runOnUiThread { toast(ex.message ?: "Could not read the file") }
                return@Thread
            }

            if (text == null) {
                runOnUiThread { toast("CSV is larger than 5 MB") }
                return@Thread
            }

            val raw = try {
                CsvRecipientParser.parse(text)
            } catch (ex: Exception) {
                runOnUiThread { toast(ex.message ?: "Could not read the CSV") }
                return@Thread
            }
            val checked = MobileNumberValidator.validateMany(raw)
            val samples = checked.invalidSamples.take(8).joinToString("\n") { sample ->
                "${sample.raw.ifBlank { "(blank)" }} — ${sample.reason}"
            }

            runOnUiThread {
                val session = app.bulkSession
                session.fileLabel = name.ifBlank { "Selected CSV" }
                session.total = checked.total
                session.validCount = checked.validCount
                session.invalidCount = checked.invalidCount
                session.duplicateCount = checked.duplicateCount
                session.recipients = checked.numbers
                session.resetSendCounters()
                session.invalidSamples = samples
                render()
            }
        }.start()
    }

    private fun confirmAndSend() {
        val session = app.bulkSession
        if (session.running.get()) return
        val message = binding.inputMessage.text?.toString()?.trim().orEmpty()
        if (session.recipients.isEmpty()) {
            toast("Choose a CSV with at least one valid mobile number")
            return
        }
        if (message.isBlank()) {
            toast("Type the message first")
            return
        }

        val gatewayNote = if (GatewayForegroundService.isRunning()) {
            "\n\nThe PC gateway is still on. Stop it on the main screen if the computer is also sending."
        } else {
            ""
        }

        AlertDialog.Builder(this)
            .setTitle("Send ${session.validCount} SMS?")
            .setMessage(
                "Each message goes out through the SIM in this phone, one number at a time. The PC is not used.$gatewayNote"
            )
            .setNegativeButton("Cancel", null)
            .setPositiveButton("Send") { _, _ ->
                session.message = message
                session.resetSendCounters()
                session.statusLine = "Starting…"
                CsvBulkSendService.start(this)
                render()
            }
            .show()
    }

    private fun render() {
        val session = app.bulkSession
        val sending = session.running.get()
        if (sending) {
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }

        binding.txtFile.text = session.fileLabel.ifBlank { "No file selected" }
        binding.txtCounts.text =
            "Total: ${session.total}\n" +
                "Valid: ${session.validCount}\n" +
                "Invalid: ${session.invalidCount}\n" +
                "Duplicates: ${session.duplicateCount}"
        binding.txtSamples.text = session.invalidSamples

        val progress = when {
            sending -> "Sending ${(session.sent + 1).coerceAtMost(session.validCount.coerceAtLeast(1))} / ${session.validCount}"
            session.statusLine.isNotBlank() -> session.statusLine
            else -> "Not sending"
        }
        binding.txtProgress.text = progress
        val current = if (session.current.isBlank()) "—" else session.current
        val error = if (session.lastError.isBlank()) "" else "\nLast error: ${session.lastError}"
        binding.txtProgressDetail.text =
            "Successful: ${session.success}\nFailed: ${session.failed}\nCurrent: $current$error"

        binding.btnPickCsv.isEnabled = !sending
        binding.btnSend.isEnabled = !sending && session.recipients.isNotEmpty()
        binding.btnStop.isEnabled = sending
        binding.inputMessage.isEnabled = !sending
        renderMessageMeta()
    }

    override fun onPause() {
        app.bulkSession.message = binding.inputMessage.text?.toString().orEmpty()
        super.onPause()
    }

    private fun renderMessageMeta() {
        val message = binding.inputMessage.text?.toString().orEmpty()
        val unicode = message.any { it.code > 127 }
        val limit = if (unicode) 70 else 160
        val kind = if (unicode) "Unicode" else "GSM"
        binding.txtMessageMeta.text = "${message.length} characters · $kind · about $limit per SMS"
    }

    private fun displayName(uri: Uri): String {
        contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) {
                val index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                if (index >= 0) return cursor.getString(index) ?: ""
            }
        }
        return uri.lastPathSegment ?: ""
    }

    private fun toast(msg: String) {
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
    }

    private fun readLimited(input: java.io.InputStream, maxBytes: Int): String? {
        val buffer = ByteArray(8192)
        val out = java.io.ByteArrayOutputStream()
        while (true) {
            val read = input.read(buffer)
            if (read < 0) break
            if (out.size() + read > maxBytes) return null
            out.write(buffer, 0, read)
        }
        return String(out.toByteArray(), Charset.forName("UTF-8"))
    }
}

private class SimpleTextWatcher(
    private val onChange: () -> Unit
) : android.text.TextWatcher {
    override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) = Unit
    override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) = Unit
    override fun afterTextChanged(s: android.text.Editable?) = onChange()
}
