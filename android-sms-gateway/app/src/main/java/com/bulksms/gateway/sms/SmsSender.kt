package com.bulksms.gateway.sms

import android.Manifest
import android.app.Activity
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.os.Build
import android.telephony.SmsManager
import androidx.core.content.ContextCompat
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

data class SmsSendOutcome(
    val success: Boolean,
    val error: String? = null
)

class SmsSender(private val context: Context) {

    fun hasPermission(): Boolean {
        return ContextCompat.checkSelfPermission(context, Manifest.permission.SEND_SMS) ==
            PackageManager.PERMISSION_GRANTED
    }

    /**
     * Sends SMS via the default subscription SmsManager and waits briefly for sent result.
     * Does not bypass Android or carrier limits.
     */
    fun send(phoneNumber: String, message: String): SmsSendOutcome {
        if (!hasPermission()) {
            return SmsSendOutcome(false, "SMS permission denied")
        }
        if (phoneNumber.isBlank()) {
            return SmsSendOutcome(false, "Phone number is required")
        }
        if (message.isBlank()) {
            return SmsSendOutcome(false, "Message is required")
        }

        return try {
            val smsManager = getSmsManager()
            val sentAction = "com.bulksms.gateway.SMS_SENT_${System.nanoTime()}"
            val latch = CountDownLatch(1)
            val resultRef = AtomicReference("PENDING")

            val receiver = object : BroadcastReceiver() {
                override fun onReceive(ctx: Context?, intent: Intent?) {
                    resultRef.set(
                        when (resultCode) {
                            Activity.RESULT_OK -> "OK"
                            SmsManager.RESULT_ERROR_GENERIC_FAILURE -> "Generic failure"
                            SmsManager.RESULT_ERROR_NO_SERVICE -> "No service"
                            SmsManager.RESULT_ERROR_NULL_PDU -> "Null PDU"
                            SmsManager.RESULT_ERROR_RADIO_OFF -> "Radio off"
                            else -> "SMS send failed ($resultCode)"
                        }
                    )
                    latch.countDown()
                }
            }

            val filter = IntentFilter(sentAction)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                context.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
            } else {
                @Suppress("UnspecifiedRegisterReceiverFlag")
                context.registerReceiver(receiver, filter)
            }

            val sentIntent = PendingIntent.getBroadcast(
                context,
                0,
                Intent(sentAction),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )

            try {
                val parts = smsManager.divideMessage(message)
                if (parts.size <= 1) {
                    smsManager.sendTextMessage(phoneNumber, null, message, sentIntent, null)
                    val completed = latch.await(20, TimeUnit.SECONDS)
                    if (!completed) {
                        // Accepted by telephony stack; delivery not guaranteed
                        return SmsSendOutcome(true, null)
                    }
                    val result = resultRef.get()
                    return if (result == "OK") {
                        SmsSendOutcome(true, null)
                    } else {
                        SmsSendOutcome(false, result)
                    }
                }

                // Multipart: hand off to telephony; Android may throttle long Unicode messages
                smsManager.sendMultipartTextMessage(phoneNumber, null, parts, null, null)
                return SmsSendOutcome(true, null)
            } finally {
                try {
                    context.unregisterReceiver(receiver)
                } catch (_: Exception) {
                }
            }
        } catch (ex: SecurityException) {
            SmsSendOutcome(false, "SMS permission denied")
        } catch (ex: Exception) {
            SmsSendOutcome(false, ex.message ?: "SMS sending failed")
        }
    }

    @Suppress("DEPRECATION")
    private fun getSmsManager(): SmsManager {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            context.getSystemService(SmsManager::class.java) ?: SmsManager.getDefault()
        } else {
            SmsManager.getDefault()
        }
    }
}
