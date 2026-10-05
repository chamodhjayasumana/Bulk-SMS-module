package com.bulksms.gateway.bulk

import java.util.concurrent.atomic.AtomicBoolean

/**
 * Phone-only CSV send state. The PC gateway HTTP server does not read this.
 */
class BulkSendSession {
    val running = AtomicBoolean(false)
    val cancel = AtomicBoolean(false)

    @Volatile var fileLabel: String = ""
    @Volatile var total: Int = 0
    @Volatile var validCount: Int = 0
    @Volatile var invalidCount: Int = 0
    @Volatile var duplicateCount: Int = 0
    @Volatile var recipients: List<String> = emptyList()
    @Volatile var message: String = ""
    @Volatile var sent: Int = 0
    @Volatile var success: Int = 0
    @Volatile var failed: Int = 0
    @Volatile var current: String = ""
    @Volatile var statusLine: String = ""
    @Volatile var lastError: String = ""
    @Volatile var invalidSamples: String = ""

    fun resetSendCounters() {
        sent = 0
        success = 0
        failed = 0
        current = ""
        lastError = ""
        statusLine = ""
        cancel.set(false)
    }
}
