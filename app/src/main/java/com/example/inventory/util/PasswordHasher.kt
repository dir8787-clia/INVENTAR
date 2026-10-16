package com.example.inventory.util

import android.content.Context
import androidx.core.content.edit
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import java.security.MessageDigest
import java.security.SecureRandom
import android.util.Base64

/**
 * Хранение пароля: в EncryptedSharedPreferences лежат ТОЛЬКО
 * хэш SHA-256(password + salt) и сам salt (Base64). Сам пароль не сохраняется.
 */
object PasswordHasher {

    private const val PREFS = "auth_prefs"
    private const val KEY_HASH = "password_hash"
    private const val KEY_SALT = "password_salt"

    private fun prefs(context: Context): android.content.SharedPreferences {
        val key = MasterKey.Builder(context, "inventory_master_key")
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
        return EncryptedSharedPreferences.create(
            context,
            PREFS,
            key,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    fun hash(password: String, saltB64: String): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val bytes = digest.digest((password + saltB64).toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(bytes, Base64.NO_WRAP)
    }

    fun generateSalt(): String {
        val b = ByteArray(32)
        SecureRandom().nextBytes(b)
        return Base64.encodeToString(b, Base64.NO_WRAP)
    }

    fun hasPassword(context: Context): Boolean =
        prefs(context).contains(KEY_HASH)

    fun setPassword(context: Context, password: String) {
        val salt = generateSalt()
        val h = hash(password, salt)
        prefs(context).edit {
            putString(KEY_SALT, salt)
            putString(KEY_HASH, h)
        }
    }

    fun checkPassword(context: Context, password: String): Boolean {
        val p = prefs(context)
        val salt = p.getString(KEY_SALT, null) ?: return false
        val expected = p.getString(KEY_HASH, null) ?: return false
        val actual = hash(password, salt)
        // constant-time сравнение
        return MessageDigest.isEqual(
            expected.toByteArray(Charsets.UTF_8),
            actual.toByteArray(Charsets.UTF_8)
        )
    }

    fun clearPassword(context: Context) {
        prefs(context).edit {
            remove(KEY_SALT)
            remove(KEY_HASH)
        }
    }

    /** Валидация нового пароля по ТЗ: 4–20 символов. */
    fun isValidPassword(pw: String): Boolean = pw.length in 4..20
}
