package com.bulksms.gateway.security

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import java.security.SecureRandom

/**
 * Stores the gateway Bearer token in EncryptedSharedPreferences.
 * Token is generated on first launch if missing — never hard-coded.
 */
class TokenStore(context: Context) {
    private val prefs = EncryptedSharedPreferences.create(
        context,
        "sms_gateway_secure_prefs",
        MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build(),
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    fun getOrCreateToken(): String {
        val existing = prefs.getString(KEY_TOKEN, null)
        if (!existing.isNullOrBlank()) return existing

        val generated = generateToken()
        prefs.edit().putString(KEY_TOKEN, generated).apply()
        return generated
    }

    fun getToken(): String = getOrCreateToken()

    fun rotateToken(): String {
        val generated = generateToken()
        prefs.edit().putString(KEY_TOKEN, generated).apply()
        return generated
    }

    fun matches(bearerToken: String?): Boolean {
        if (bearerToken.isNullOrBlank()) return false
        return bearerToken.trim() == getToken()
    }

    private fun generateToken(): String {
        val bytes = ByteArray(32)
        SecureRandom().nextBytes(bytes)
        return bytes.joinToString("") { "%02x".format(it) }
    }

    companion object {
        private const val KEY_TOKEN = "gateway_api_token"
    }
}
