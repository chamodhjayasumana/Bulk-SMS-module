package com.bulksms.gateway.server

import android.content.Context
import android.util.Log
import com.bulksms.gateway.SmsGatewayApp
import com.bulksms.gateway.data.RecentMessage
import com.bulksms.gateway.sms.SmsSender
import com.bulksms.gateway.util.NetworkUtils
import fi.iki.elonen.NanoHTTPD
import org.json.JSONObject
import java.io.IOException

class GatewayHttpServer(
    private val appContext: Context,
    port: Int,
    private val onActivity: (() -> Unit)? = null
) : NanoHTTPD(port) {

    private val app get() = appContext.applicationContext as SmsGatewayApp
    private val smsSender = SmsSender(appContext)

    override fun serve(session: IHTTPSession): Response {
        return try {
            val uri = session.uri.trimEnd('/').ifEmpty { "/" }
            val method = session.method

            if (!isAuthorized(session)) {
                return jsonResponse(
                    Response.Status.UNAUTHORIZED,
                    JSONObject()
                        .put("success", false)
                        .put("error", "Unauthorized")
                )
            }

            when {
                method == Method.GET && uri == "/api/gateway/status" -> handleStatus()
                method == Method.POST && uri == "/api/sms/send" -> handleSend(session)
                else -> jsonResponse(
                    Response.Status.NOT_FOUND,
                    JSONObject().put("success", false).put("error", "Not found")
                )
            }
        } catch (ex: Exception) {
            Log.e(TAG, "Request failed", ex)
            jsonResponse(
                Response.Status.INTERNAL_ERROR,
                JSONObject().put("success", false).put("error", "Internal error")
            )
        }
    }

    private fun isAuthorized(session: IHTTPSession): Boolean {
        val header = session.headers["authorization"] ?: return false
        val token = if (header.startsWith("Bearer ", ignoreCase = true)) {
            header.substring(7).trim()
        } else {
            header.trim()
        }
        return app.tokenStore.matches(token)
    }

    private fun handleStatus(): Response {
        val body = JSONObject()
            .put("online", app.stats.online.get())
            .put("networkAvailable", NetworkUtils.isNetworkAvailable(appContext))
            .put("wifiConnected", NetworkUtils.isWifiConnected(appContext))
            .put("simOperator", NetworkUtils.getSimOperatorName(appContext))
            .put("ipAddress", NetworkUtils.getLocalIpAddress())
            .put("port", listeningPort)
            .put("messagesSent", app.stats.messagesSent.get())
            .put("successful", app.stats.successful.get())
            .put("failed", app.stats.failed.get())
            .put("smsPermissionGranted", smsSender.hasPermission())
        return jsonResponse(Response.Status.OK, body)
    }

    private fun handleSend(session: IHTTPSession): Response {
        val bodyMap = HashMap<String, String>()
        session.parseBody(bodyMap)
        val raw = bodyMap["postData"]
            ?: session.inputStream?.bufferedReader()?.use { it.readText() }
            ?: ""

        if (raw.isBlank()) {
            return jsonResponse(
                Response.Status.BAD_REQUEST,
                JSONObject().put("success", false).put("error", "Empty body")
            )
        }

        val json = JSONObject(raw)
        val requestId = json.optString("requestId").trim()
        val phoneNumber = json.optString("phoneNumber").trim()
        val message = json.optString("message")

        if (requestId.isBlank()) {
            return jsonResponse(
                Response.Status.BAD_REQUEST,
                JSONObject().put("success", false).put("error", "requestId is required")
            )
        }

        // Idempotency: same requestId must not send twice
        app.stats.getPrevious(requestId)?.let { previous ->
            Log.i(TAG, "Duplicate requestId ignored: $requestId")
            return jsonResponse(
                Response.Status.OK,
                JSONObject()
                    .put("requestId", requestId)
                    .put("success", previous.success)
                    .put("duplicate", true)
                    .apply {
                        if (!previous.success && !previous.error.isNullOrBlank()) {
                            put("error", previous.error)
                        }
                    }
            )
        }

        if (!smsSender.hasPermission()) {
            val err = "SMS permission denied"
            app.stats.rememberResult(requestId, false, err)
            app.stats.messagesSent.incrementAndGet()
            app.stats.failed.incrementAndGet()
            app.stats.addRecent(RecentMessage(phoneNumber, "FAILED", requestId, err))
            onActivity?.invoke()
            return jsonResponse(
                Response.Status.FORBIDDEN,
                JSONObject()
                    .put("requestId", requestId)
                    .put("success", false)
                    .put("error", err)
            )
        }

        val outcome = smsSender.send(phoneNumber, message)
        app.stats.rememberResult(requestId, outcome.success, outcome.error)
        app.stats.messagesSent.incrementAndGet()
        if (outcome.success) {
            app.stats.successful.incrementAndGet()
            app.stats.addRecent(RecentMessage(phoneNumber, "SENT", requestId))
        } else {
            app.stats.failed.incrementAndGet()
            app.stats.addRecent(
                RecentMessage(phoneNumber, "FAILED", requestId, outcome.error ?: "SMS sending failed")
            )
        }
        onActivity?.invoke()

        return jsonResponse(
            if (outcome.success) Response.Status.OK else Response.Status.BAD_REQUEST,
            JSONObject()
                .put("requestId", requestId)
                .put("success", outcome.success)
                .apply {
                    if (!outcome.success) put("error", outcome.error ?: "SMS sending failed")
                }
        )
    }

    private fun jsonResponse(status: Response.Status, body: JSONObject): Response {
        return newFixedLengthResponse(status, "application/json", body.toString())
    }

    fun startServer() {
        if (wasStarted()) return
        try {
            start(SOCKET_READ_TIMEOUT, false)
            app.stats.online.set(true)
            Log.i(TAG, "Gateway listening on port $listeningPort")
        } catch (ex: IOException) {
            app.stats.online.set(false)
            throw ex
        }
    }

    fun stopServer() {
        stop()
        app.stats.online.set(false)
        Log.i(TAG, "Gateway stopped")
    }

    companion object {
        private const val TAG = "GatewayHttpServer"
        const val DEFAULT_PORT = 8080
    }
}
