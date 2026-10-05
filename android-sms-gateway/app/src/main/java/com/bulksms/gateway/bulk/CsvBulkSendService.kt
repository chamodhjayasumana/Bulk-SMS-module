package com.bulksms.gateway.bulk

import android.app.Notification
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import com.bulksms.gateway.R
import com.bulksms.gateway.SmsGatewayApp
import com.bulksms.gateway.data.RecentMessage
import com.bulksms.gateway.sms.SmsSender
import com.bulksms.gateway.ui.BulkSendActivity
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Sends the prepared CSV list one number at a time through the phone SIM.
 * Does not open the LAN gateway and does not call the PC API.
 */
class CsvBulkSendService : Service() {

    private val app get() = application as SmsGatewayApp
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                app.bulkSession.cancel.set(true)
                if (!workerStarted.get()) stopSelf()
            }
            else -> beginSend()
        }
        return START_NOT_STICKY
    }

    private fun beginSend() {
        if (!workerStarted.compareAndSet(false, true)) {
            return
        }

        val session = app.bulkSession
        val numbers = session.recipients.toList()
        val message = session.message
        if (numbers.isEmpty() || message.isBlank()) {
            workerStarted.set(false)
            session.running.set(false)
            session.statusLine = "Nothing to send"
            publish()
            stopSelf()
            return
        }

        session.cancel.set(false)
        session.running.set(true)
        session.resetSendCounters()
        session.running.set(true)

        val notification = buildNotification(0, numbers.size)
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                ServiceCompat.startForeground(
                    this,
                    NOTIFICATION_ID,
                    notification,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE
                )
            } else {
                startForeground(NOTIFICATION_ID, notification)
            }
        } catch (ex: Exception) {
            Log.e(TAG, "Could not start CSV send foreground", ex)
            workerStarted.set(false)
            session.running.set(false)
            session.statusLine = ex.message ?: "Could not start sending"
            publish()
            stopSelf()
            return
        }

        acquireWakeLock()
        Thread {
            try {
                sendAll(numbers, message)
            } finally {
                releaseWakeLock()
                session.running.set(false)
                session.current = ""
                workerStarted.set(false)
                publish()
                stopForeground(STOP_FOREGROUND_REMOVE)
                stopSelf()
            }
        }.start()
    }

    private fun sendAll(numbers: List<String>, message: String) {
        val session = app.bulkSession
        val sender = SmsSender(this)
        val day = SimpleDateFormat("yyyyMMdd", Locale.US).format(Date())

        for ((index, number) in numbers.withIndex()) {
            if (session.cancel.get()) {
                session.statusLine = "Stopped after ${session.sent} of ${numbers.size}"
                return
            }

            session.current = number
            try {
                updateNotification(index, numbers.size)
            } catch (_: Exception) {
            }
            publish()

            val outcome = sender.send(number, message)
            session.sent = index + 1
            val requestId = "PHONE-$day-${(index + 1).toString().padStart(6, '0')}"
            app.stats.messagesSent.incrementAndGet()
            if (outcome.success) {
                session.success++
                app.stats.successful.incrementAndGet()
                app.stats.addRecent(RecentMessage(number, "SENT", requestId))
            } else {
                session.failed++
                session.lastError = outcome.error ?: "Failed"
                app.stats.failed.incrementAndGet()
                app.stats.addRecent(
                    RecentMessage(number, "FAILED", requestId, outcome.error)
                )
            }
            publish()

            val hasMore = index < numbers.lastIndex && !session.cancel.get()
            if (hasMore) {
                try {
                    Thread.sleep(DELAY_BETWEEN_MESSAGES_MS)
                } catch (_: InterruptedException) {
                    session.cancel.set(true)
                }
            }
        }

        session.statusLine = if (session.cancel.get()) {
            "Stopped after ${session.sent} of ${numbers.size}"
        } else {
            "Finished. Successful ${session.success}, failed ${session.failed}."
        }
    }

    private fun acquireWakeLock() {
        val power = getSystemService(POWER_SERVICE) as PowerManager
        wakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "bulksms:csv-send").apply {
            setReferenceCounted(false)
            acquire(MAX_WAKE_MS)
        }
    }

    private fun releaseWakeLock() {
        try {
            if (wakeLock?.isHeld == true) wakeLock?.release()
        } catch (_: Exception) {
        }
        wakeLock = null
    }

    private fun updateNotification(index: Int, total: Int) {
        val manager = getSystemService(NOTIFICATION_SERVICE) as android.app.NotificationManager
        manager.notify(NOTIFICATION_ID, buildNotification(index, total))
    }

    private fun buildNotification(index: Int, total: Int): Notification {
        val openIntent = PendingIntent.getActivity(
            this,
            2,
            Intent(this, BulkSendActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val stopIntent = PendingIntent.getService(
            this,
            3,
            Intent(this, CsvBulkSendService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val shown = (index + 1).coerceAtMost(total)
        return NotificationCompat.Builder(this, getString(R.string.bulk_notification_channel_id))
            .setContentTitle(getString(R.string.bulk_notification_title))
            .setContentText("Sending $shown of $total")
            .setSmallIcon(R.drawable.ic_launcher)
            .setContentIntent(openIntent)
            .addAction(0, "Stop", stopIntent)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .build()
    }

    private fun publish() {
        sendBroadcast(Intent(ACTION_PROGRESS).setPackage(packageName))
    }

    companion object {
        private const val TAG = "CsvBulkSend"
        private const val NOTIFICATION_ID = 1002
        private const val DELAY_BETWEEN_MESSAGES_MS = 1_000L
        private const val MAX_WAKE_MS = 6L * 60L * 60L * 1000L

        const val ACTION_START = "com.bulksms.gateway.CSV_SEND_START"
        const val ACTION_STOP = "com.bulksms.gateway.CSV_SEND_STOP"
        const val ACTION_PROGRESS = "com.bulksms.gateway.CSV_SEND_PROGRESS"

        private val workerStarted = AtomicBoolean(false)

        fun isWorking(): Boolean = workerStarted.get()

        fun start(context: Context) {
            val intent = Intent(context, CsvBulkSendService::class.java).setAction(ACTION_START)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }

        fun stop(context: Context) {
            val intent = Intent(context, CsvBulkSendService::class.java).setAction(ACTION_STOP)
            context.startService(intent)
        }
    }
}
