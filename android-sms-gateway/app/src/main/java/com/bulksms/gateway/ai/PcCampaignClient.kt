package com.bulksms.gateway.ai

import org.json.JSONObject
import java.io.BufferedReader
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.URL

/**
 * Asks the PC API for campaign suggestions. The AI key stays on the PC.
 * This client never sends SMS.
 */
class PcCampaignClient {

    data class Suggestion(
        val message: String,
        val characterCount: Int,
        val segments: Int,
        val warnings: List<String>
    )

    data class Draft(
        val suggestions: List<Suggestion>,
        val safetyWarnings: List<String>
    )

    fun login(baseUrl: String, username: String, password: String): String {
        val body = JSONObject()
            .put("username", username)
            .put("password", password)
        val response = post(baseUrl, "/api/Auth/token", body, token = null)
        val data = response.optJSONObject("data")
        val token = data?.optString("token").orEmpty()
        if (!response.optBoolean("success") || token.isBlank()) {
            throw IllegalStateException(messageOf(response, "Sign-in failed."))
        }
        return token
    }

    fun draft(
        baseUrl: String,
        token: String,
        description: String,
        language: String,
        senderName: String,
        maxSegments: Int,
        tone: String,
        includeCallToAction: Boolean,
        personalizationFields: List<String>
    ): Draft {
        val fields = org.json.JSONArray()
        personalizationFields.forEach { fields.put(it) }
        val body = JSONObject()
            .put("campaignDescription", description)
            .put("language", language)
            .put("senderName", senderName)
            .put("maxSegments", maxSegments)
            .put("tone", tone)
            .put("includeCallToAction", includeCallToAction)
            .put("personalizationFields", fields)
        val response = post(baseUrl, "/api/ai/campaign-draft", body, token)
        if (!response.optBoolean("success")) {
            throw IllegalStateException(messageOf(response, "Could not create suggestions."))
        }
        val data = response.optJSONObject("data")
            ?: throw IllegalStateException("The PC returned an unexpected response.")
        val suggestions = data.optJSONArray("suggestions")
        val parsed = ArrayList<Suggestion>()
        if (suggestions != null) {
            for (i in 0 until suggestions.length()) {
                val item = suggestions.optJSONObject(i) ?: continue
                val warnings = ArrayList<String>()
                val warningArray = item.optJSONArray("warnings")
                if (warningArray != null) {
                    for (w in 0 until warningArray.length()) {
                        val text = warningArray.optString(w)
                        if (text.isNotBlank()) warnings.add(text)
                    }
                }
                val message = item.optString("message")
                if (message.isBlank()) continue
                parsed.add(
                    Suggestion(
                        message = message,
                        characterCount = item.optInt("characterCount"),
                        segments = item.optInt("segments"),
                        warnings = warnings
                    )
                )
            }
        }
        val safety = ArrayList<String>()
        val safetyArray = data.optJSONArray("safetyWarnings")
        if (safetyArray != null) {
            for (i in 0 until safetyArray.length()) {
                val text = safetyArray.optString(i)
                if (text.isNotBlank()) safety.add(text)
            }
        }
        if (parsed.isEmpty()) {
            throw IllegalStateException(messageOf(response, "No suggestions were returned."))
        }
        return Draft(parsed, safety)
    }

    private fun post(baseUrl: String, path: String, body: JSONObject, token: String?): JSONObject {
        val root = normalize(baseUrl)
        val connection = (URL(root + path).openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 8_000
            readTimeout = 30_000
            doOutput = true
            setRequestProperty("Content-Type", "application/json")
            setRequestProperty("Accept", "application/json")
            if (!token.isNullOrBlank()) {
                setRequestProperty("Authorization", "Bearer $token")
            }
        }
        try {
            OutputStreamWriter(connection.outputStream, Charsets.UTF_8).use { it.write(body.toString()) }
            val code = connection.responseCode
            val stream = if (code in 200..299) connection.inputStream else connection.errorStream
            val text = stream?.bufferedReader()?.use(BufferedReader::readText).orEmpty()
            if (text.isBlank()) {
                throw IllegalStateException(if (code == 401) "Sign-in failed." else "The PC did not answer ($code).")
            }
            val json = JSONObject(text)
            if (code == 401) throw IllegalStateException(messageOf(json, "Sign-in failed."))
            if (code !in 200..299 && !json.has("success")) {
                throw IllegalStateException(messageOf(json, "The PC could not create suggestions ($code)."))
            }
            return json
        } finally {
            connection.disconnect()
        }
    }

    private fun normalize(baseUrl: String): String {
        val value = baseUrl.trim().trimEnd('/')
        if (value.isBlank() || !(value.startsWith("http://") || value.startsWith("https://"))) {
            throw IllegalArgumentException("Enter the PC address, for example http://192.168.8.100:5219")
        }
        if (value.contains("localhost", ignoreCase = true) || value.contains("127.0.0.1")) {
            throw IllegalArgumentException("On the phone, use the PC's Wi-Fi address, not localhost.")
        }
        return value
    }

    private fun messageOf(json: JSONObject, fallback: String): String {
        val message = json.optString("message")
        return if (message.isBlank()) fallback else message
    }
}
