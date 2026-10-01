package com.bulksms.gateway.data

import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

data class RecentMessage(
    val phoneNumber: String,
    val status: String,
    val requestId: String,
    val error: String? = null,
    val timestampMs: Long = System.currentTimeMillis()
)

class GatewayStats {
    val online = AtomicBoolean(false)
    val messagesSent = AtomicInteger(0)
    val successful = AtomicInteger(0)
    val failed = AtomicInteger(0)
    val recent = CopyOnWriteArrayList<RecentMessage>()

    /** Idempotency map: requestId -> previous result JSON fields */
    private val idempotency = ConcurrentHashMap<String, IdempotentResult>()

    data class IdempotentResult(
        val success: Boolean,
        val error: String?
    )

    fun rememberResult(requestId: String, success: Boolean, error: String?): IdempotentResult {
        val result = IdempotentResult(success, error)
        idempotency[requestId] = result
        return result
    }

    fun getPrevious(requestId: String): IdempotentResult? = idempotency[requestId]

    fun addRecent(item: RecentMessage) {
        recent.add(0, item)
        while (recent.size > 50) {
            recent.removeAt(recent.lastIndex)
        }
    }

    fun resetCounters() {
        messagesSent.set(0)
        successful.set(0)
        failed.set(0)
        recent.clear()
        idempotency.clear()
    }
}
