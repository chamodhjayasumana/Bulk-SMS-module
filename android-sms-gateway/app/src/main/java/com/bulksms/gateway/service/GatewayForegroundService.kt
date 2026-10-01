package com.bulksms.gateway.service

import android.app.Notification
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import com.bulksms.gateway.R
import com.bulksms.gateway.SmsGatewayApp
import com.bulksms.gateway.server.GatewayHttpServer
import com.bulksms.gateway.ui.MainActivity
import java.util.concurrent.atomic.AtomicReference

class GatewayForegroundService : Service() {

    private var server: GatewayHttpServer? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                stopGateway()
                stopSelf()
                return START_NOT_STICKY
            }
            else -> startGateway(intent?.getIntExtra(EXTRA_PORT, GatewayHttpServer.DEFAULT_PORT)
                ?: GatewayHttpServer.DEFAULT_PORT)
        }
        return START_STICKY
    }

    private fun startGateway(port: Int) {
        if (server?.wasStarted() == true) {
            publishRunningState(true, port)
            return
        }

        val notification = buildNotification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            ServiceCompat.startForeground(
                this,
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE
            )
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }

        try {
            val http = GatewayHttpServer(applicationContext, port) {
                sendBroadcast(Intent(ACTION_STATS_CHANGED).setPackage(packageName))
            }
            http.startServer()
            server = http
            activeServer.set(http)
            publishRunningState(true, port)
            Log.i(TAG, "Foreground gateway started on $port")
        } catch (ex: Exception) {
            Log.e(TAG, "Failed to start gateway", ex)
            publishRunningState(false, port, ex.message)
            stopSelf()
        }
    }

    private fun stopGateway() {
        try {
            server?.stopServer()
        } catch (_: Exception) {
        }
        server = null
        activeServer.set(null)
        (application as SmsGatewayApp).stats.online.set(false)
        publishRunningState(false, GatewayHttpServer.DEFAULT_PORT)
        stopForeground(STOP_FOREGROUND_REMOVE)
        Log.i(TAG, "Foreground gateway stopped")
    }

    override fun onDestroy() {
        stopGateway()
        super.onDestroy()
    }

    private fun buildNotification(): Notification {
        val openIntent = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val stopIntent = PendingIntent.getService(
            this,
            1,
            Intent(this, GatewayForegroundService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        return NotificationCompat.Builder(this, getString(R.string.notification_channel_id))
            .setContentTitle(getString(R.string.notification_title))
            .setContentText(getString(R.string.notification_text))
            .setSmallIcon(R.drawable.ic_launcher)
            .setContentIntent(openIntent)
            .addAction(0, "Stop", stopIntent)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .build()
    }

    private fun publishRunningState(running: Boolean, port: Int, error: String? = null) {
        val intent = Intent(ACTION_STATE_CHANGED).setPackage(packageName)
            .putExtra(EXTRA_RUNNING, running)
            .putExtra(EXTRA_PORT, port)
            .putExtra(EXTRA_ERROR, error)
        sendBroadcast(intent)
    }

    companion object {
        private const val TAG = "GatewayFgService"
        private const val NOTIFICATION_ID = 1001

        const val ACTION_START = "com.bulksms.gateway.START"
        const val ACTION_STOP = "com.bulksms.gateway.STOP"
        const val ACTION_STATE_CHANGED = "com.bulksms.gateway.STATE_CHANGED"
        const val ACTION_STATS_CHANGED = "com.bulksms.gateway.STATS_CHANGED"
        const val EXTRA_PORT = "port"
        const val EXTRA_RUNNING = "running"
        const val EXTRA_ERROR = "error"

        private val activeServer = AtomicReference<GatewayHttpServer?>(null)

        fun isRunning(): Boolean = activeServer.get()?.wasStarted() == true

        fun start(context: Context, port: Int = GatewayHttpServer.DEFAULT_PORT) {
            val intent = Intent(context, GatewayForegroundService::class.java)
                .setAction(ACTION_START)
                .putExtra(EXTRA_PORT, port)
            ContextCompatStart(context, intent)
        }

        fun stop(context: Context) {
            val intent = Intent(context, GatewayForegroundService::class.java)
                .setAction(ACTION_STOP)
            ContextCompatStart(context, intent)
        }

        private fun ContextCompatStart(context: Context, intent: Intent) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }
    }
}
