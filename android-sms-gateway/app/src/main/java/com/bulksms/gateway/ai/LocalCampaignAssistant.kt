package com.bulksms.gateway.ai

import kotlin.math.ceil

/**
 * Writes campaign suggestions on the phone. It does not call the computer or an external AI service.
 */
class LocalCampaignAssistant {

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

    fun draft(
        description: String,
        language: String,
        senderName: String,
        maxSegments: Int,
        tone: String,
        includeCallToAction: Boolean,
        placeholders: List<String>
    ): Draft {
        val topic = description.trim()
        if (topic.isEmpty()) {
            return Draft(emptyList(), listOf("Describe the campaign first."))
        }
        if (topic.length > 2000) {
            return Draft(emptyList(), listOf("The description is too long. Keep it under 2000 characters."))
        }
        if (isSevere(topic)) {
            return Draft(emptyList(), listOf("This description looks unsafe. Change it before creating suggestions."))
        }

        val allowed = placeholders.map { it.trim() }.filter { it.isNotEmpty() }
        val messages = (0 until 3).map { index ->
            build(topic, language, senderName, tone, includeCallToAction, allowed, index)
        }
        return Draft(
            suggestions = messages.map { inspect(it, allowed, maxSegments.coerceIn(1, 5)) },
            safetyWarnings = emptyList()
        )
    }

    private fun build(
        topic: String,
        language: String,
        senderName: String,
        tone: String,
        includeCallToAction: Boolean,
        placeholders: List<String>,
        index: Int
    ): String {
        val sender = senderName.trim().let { if (it.isEmpty()) "" else "$it:" }
        val shortTopic = if (topic.length <= 48) topic else topic.take(48).trimEnd()
        val greeting = if (has(placeholders, "name")) greeting(language) else ""
        val extra = placeholders
            .filter { !it.equals("name", ignoreCase = true) }
            .joinToString(" ") { "{$it}" }
        val lead = lead(language, tone, index)
        val cta = if (includeCallToAction) callToAction(language, tone) else ""
        return listOf(sender, greeting, lead, shortTopic, extra, cta)
            .filter { it.isNotBlank() }
            .joinToString(" ")
    }

    private fun inspect(message: String, allowed: List<String>, maxSegments: Int): Suggestion {
        val warnings = ArrayList<String>()
        if (isSevere(message)) {
            warnings.add("This suggestion was left out because it looks unsafe.")
            return Suggestion("", message.length, 0, warnings)
        }
        val segments = estimateSegments(message)
        if (segments > maxSegments) {
            warnings.add("This message uses $segments SMS parts. The maximum you chose is $maxSegments.")
        }
        PLACEHOLDER.findAll(message).map { it.groupValues[1] }
            .filter { name -> allowed.none { it.equals(name, ignoreCase = true) } }
            .forEach { warnings.add("Remove {$it} or turn that name on before sending.") }
        return Suggestion(message, message.length, segments, warnings)
    }

    private fun isSevere(text: String): Boolean {
        if (SECRET.containsMatchIn(text) || OTP.containsMatchIn(text)) return true
        val lower = text.lowercase()
        return ABUSIVE.any { lower.contains(it) }
    }

    private fun has(fields: List<String>, name: String) =
        fields.any { it.equals(name, ignoreCase = true) }

    private fun greeting(language: String) = when (language) {
        "si" -> "ආයුබෝවන් {name}."
        "ta" -> "வணக்கம் {name}."
        else -> "Hello {name}."
    }

    private fun lead(language: String, tone: String, index: Int): String {
        if (language == "si") {
            return when (index) {
                1 -> "විශේෂ දැනුම්දීම."
                2 -> if (tone == "urgent") "අද පමණයි." else "ඔබ වෙනුවෙන්."
                else -> "පණිවිඩය."
            }
        }
        if (language == "ta") {
            return when (index) {
                1 -> "சிறப்பு அறிவிப்பு."
                2 -> if (tone == "urgent") "இன்று மட்டும்." else "உங்களுக்காக."
                else -> "செய்தி."
            }
        }
        return when (index) {
            1 -> if (tone == "friendly") "A note from us." else "An update for you."
            2 -> if (tone == "promotional") "A special offer." else "Please read this."
            else -> if (tone == "urgent") "Time-sensitive update." else "Campaign message."
        }
    }

    private fun callToAction(language: String, tone: String): String {
        if (language == "si") {
            return if (tone == "urgent") "අදම පිළිතුරු දෙන්න." else "විස්තර සඳහා පිළිතුරු දෙන්න."
        }
        if (language == "ta") {
            return if (tone == "urgent") "இன்று பதிலளியுங்கள்." else "விவரங்களுக்கு பதிலளியுங்கள்."
        }
        return when (tone) {
            "friendly" -> "Come visit us."
            "urgent" -> "Reply today."
            "promotional" -> "Ask us for the offer."
            else -> "Reply YES for details."
        }
    }

    private fun estimateSegments(message: String): Int {
        if (message.isEmpty()) return 0
        val unicode = message.any { it !in GSM7_BASIC && it !in GSM7_EXTENDED }
        val units = if (unicode) message.length else message.sumOf { if (it in GSM7_EXTENDED) 2L else 1L }.toInt()
        return if (!unicode) {
            if (units <= 160) 1 else ceil(units / 153.0).toInt()
        } else {
            if (units <= 70) 1 else ceil(units / 67.0).toInt()
        }
    }

    companion object {
        private val PLACEHOLDER = Regex("\\{([A-Za-z][A-Za-z0-9_]*)\\}")
        private val SECRET = Regex("(?i)\\b(password|api[_-]?key|secret|bearer)\\b\\s*(is|=|:)\\s*\\S+|sk-[A-Za-z0-9]{10,}")
        private val OTP = Regex("(?i)\\b(otp|one[- ]time)\\b.{0,24}\\d{4,8}")
        private val ABUSIVE = listOf("kill yourself", "bomb threat", "i will hurt you")
        private const val GSM7_BASIC =
            "@£\$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ ÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
                "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà"
        private const val GSM7_EXTENDED = "^{}\\[~]|€"
    }
}
