package com.bulksms.gateway.bulk

/**
 * Reads the same mobile columns the PC parser accepts.
 * Blank rows are skipped. Excel .xlsx files are not read here.
 */
object CsvRecipientParser {

    private val preferredHeaders = setOf(
        "mobile",
        "mobilenumber",
        "phone",
        "phonenumber",
        "contact",
        "telephone",
        "number",
        "msisdn"
    )

    fun parse(text: String): List<String> {
        val content = text.removePrefix("\uFEFF")
        if (content.isBlank()) return emptyList()

        val delimiter = detectDelimiter(content)
        val rows = readRows(content, delimiter)
            .filter { row -> row.any { it.isNotBlank() } }
        if (rows.isEmpty()) return emptyList()

        val headers = rows.first().map { it.trim() }
        val column = resolveColumn(headers)

        if (column < 0 && headers.size == 1 && looksLikeNumber(headers[0])) {
            return rows.map { it.getOrElse(0) { "" }.trim() }
        }

        val index = if (column < 0) 0 else column
        val start = if (column < 0 && looksLikeNumber(headers.getOrElse(0) { "" })) 0 else 1
        if (start >= rows.size) return emptyList()

        return rows.drop(start).map { row -> row.getOrElse(index) { "" }.trim() }
    }

    private fun resolveColumn(headers: List<String>): Int {
        headers.forEachIndexed { index, header ->
            if (header.trim().lowercase() in preferredHeaders) return index
        }
        return -1
    }

    private fun looksLikeNumber(value: String): Boolean {
        return value.count { it.isDigit() } >= 9
    }

    private fun detectDelimiter(content: String): Char {
        val line = StringBuilder()
        var quoted = false
        for (c in content) {
            if (c == '"') quoted = !quoted
            if (!quoted && (c == '\n' || c == '\r')) break
            line.append(c)
        }
        val sample = line.toString()
        val commas = sample.count { it == ',' }
        val semicolons = sample.count { it == ';' }
        val tabs = sample.count { it == '\t' }
        return when {
            semicolons > commas && semicolons >= tabs -> ';'
            tabs > commas && tabs >= semicolons -> '\t'
            else -> ','
        }
    }

    private fun readRows(content: String, delimiter: Char): List<List<String>> {
        val rows = ArrayList<List<String>>()
        val row = ArrayList<String>()
        val field = StringBuilder()
        var quoted = false
        var i = 0
        while (i < content.length) {
            val c = content[i]
            if (quoted) {
                if (c == '"') {
                    if (i + 1 < content.length && content[i + 1] == '"') {
                        field.append('"')
                        i++
                    } else {
                        quoted = false
                    }
                } else {
                    field.append(c)
                }
            } else {
                when (c) {
                    '"' -> quoted = true
                    delimiter -> {
                        row.add(field.toString())
                        field.clear()
                    }
                    '\n', '\r' -> {
                        if (c == '\r' && i + 1 < content.length && content[i + 1] == '\n') {
                            i++
                        }
                        row.add(field.toString())
                        field.clear()
                        rows.add(row.toList())
                        row.clear()
                    }
                    else -> field.append(c)
                }
            }
            i++
        }
        if (field.isNotEmpty() || row.isNotEmpty()) {
            row.add(field.toString())
            rows.add(row.toList())
        }
        return rows
    }
}
