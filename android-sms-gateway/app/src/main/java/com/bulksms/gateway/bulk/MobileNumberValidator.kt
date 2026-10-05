package com.bulksms.gateway.bulk

/**
 * Same Sri Lankan mobile rules as the PC API (07X / 947X).
 * Landlines are rejected. The PC validator is unchanged.
 */
object MobileNumberValidator {

    private val validLocalPrefixes = setOf(
        "070", "071", "072", "074", "075", "076", "077", "078"
    )

    private val landlinePrefixes = setOf(
        "011", "021", "031", "033", "034", "035", "036", "037", "038",
        "041", "045", "047", "051", "052", "054", "055", "057",
        "063", "065", "066", "067", "081", "091"
    )

    fun validateMany(rawNumbers: List<String>): CheckedNumbers {
        val seen = HashSet<String>()
        val numbers = ArrayList<String>()
        val samples = ArrayList<InvalidSample>()
        var invalid = 0
        var duplicates = 0

        for (raw in rawNumbers) {
            val item = validate(raw)
            if (!item.valid) {
                invalid++
                if (samples.size < 50) {
                    samples.add(InvalidSample(item.raw, item.reason ?: "Invalid"))
                }
                continue
            }
            val normalized = item.normalized ?: continue
            if (!seen.add(normalized)) {
                duplicates++
                continue
            }
            numbers.add(normalized)
        }

        return CheckedNumbers(
            total = rawNumbers.size,
            validCount = numbers.size,
            invalidCount = invalid,
            duplicateCount = duplicates,
            numbers = numbers,
            invalidSamples = samples
        )
    }

    fun validate(raw: String?): NumberCheck {
        val value = raw ?: ""
        if (value.isBlank()) {
            return NumberCheck(value, false, null, "Empty value")
        }

        val digits = value.trim().filter { it.isDigit() }
        if (digits.isEmpty()) {
            return NumberCheck(value, false, null, "Empty value")
        }

        if (digits.length == 10 && digits.startsWith("0")) {
            val prefix = digits.substring(0, 3)
            if (prefix !in validLocalPrefixes) {
                val reason = if (prefix in landlinePrefixes) {
                    "Landline numbers are not allowed"
                } else {
                    "Invalid mobile prefix"
                }
                return NumberCheck(value, false, null, reason)
            }
            return NumberCheck(value, true, "94" + digits.substring(1), null)
        }

        if (digits.length == 11 && digits.startsWith("94")) {
            val localStyle = "0" + digits.substring(2)
            val prefix = localStyle.substring(0, 3)
            if (prefix !in validLocalPrefixes) {
                val reason = if (prefix in landlinePrefixes) {
                    "Landline numbers are not allowed"
                } else {
                    "Invalid mobile prefix"
                }
                return NumberCheck(value, false, null, reason)
            }
            return NumberCheck(value, true, digits, null)
        }

        val reason = when {
            digits.length < 10 -> "Too-short number"
            digits.length > 11 -> "Too-long number"
            else -> "Invalid number format"
        }
        return NumberCheck(value, false, null, reason)
    }
}

data class NumberCheck(
    val raw: String,
    val valid: Boolean,
    val normalized: String?,
    val reason: String?
)

data class InvalidSample(
    val raw: String,
    val reason: String
)

data class CheckedNumbers(
    val total: Int,
    val validCount: Int,
    val invalidCount: Int,
    val duplicateCount: Int,
    val numbers: List<String>,
    val invalidSamples: List<InvalidSample>
)
