package com.bulksms.gateway

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.os.Build
import com.bulksms.gateway.data.GatewayStats
import com.bulksms.gateway.security.TokenStore

class SmsGatewayApp : Application() {
    lateinit var tokenStore: TokenStore
        private set

    val stats = GatewayStats()

    override fun onCreate() {
        super.onCreate()
        tokenStore = TokenStore(this)
        createNotificationChannel()
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                getString(R.string.notification_channel_id),
                getString(R.string.notification_channel_name),
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Shows while the local SMS gateway HTTP server is running"
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(channel)
        }
    }
}
