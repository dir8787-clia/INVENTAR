package com.example.inventory.util

import android.content.Context
import android.net.Uri
import com.example.inventory.data.local.Item
import com.example.inventory.data.local.ScanEvent
import org.json.JSONArray
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Резервные копии JSON: {version, exportDate, items[], scanEvents[]}
 * Экспорт/импорт через SAF. Импорт — два режима: «Заменить всё» / «Объединить сканы».
 */
object JsonBackup {

    const val VERSION = 1

    data class BackupData(
        val version: Int,
        val exportDate: String,
        val items: List<Item>,
        val scanEvents: List<ScanEvent>
    )

    fun exportFileName(): String {
        val d = SimpleDateFormat("yyyy-MM-dd_HH-mm", Locale.ROOT).format(Date())
        return "Инвентаризация_Бэкап_$d.json"
    }

    /** Сериализует состояние и пишет в SAF uri. Вызывать из Dispatchers.IO. */
    fun export(context: Context, uri: Uri, items: List<Item>, events: List<ScanEvent>) {
        val root = JSONObject()
        root.put("version", VERSION)
        root.put("exportDate", SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.ROOT).format(Date()))

        val itemsArr = JSONArray()
        items.forEach { it2 ->
            itemsArr.put(JSONObject().apply {
                put("invNumber", it2.invNumber)
                put("name", it2.name)
                put("unit", it2.unit)
                put("expectedQty", it2.expectedQty)
            })
        }
        root.put("items", itemsArr)

        val eventsArr = JSONArray()
        events.forEach { e ->
            eventsArr.put(JSONObject().apply {
                put("invNumber", e.invNumber)
                put("timestamp", e.timestamp)
                put("type", e.type)
                put("source", e.source)
            })
        }
        root.put("scanEvents", eventsArr)

        context.contentResolver.openOutputStream(uri)?.use { out ->
            out.write(root.toString(2).toByteArray(Charsets.UTF_8))
        } ?: throw IllegalStateException("Не удалось открыть поток записи")
    }

    /** Читает и парсит JSON-бэкап из SAF uri. Вызывать из Dispatchers.IO. */
    fun parse(context: Context, uri: Uri): BackupData {
        val text = context.contentResolver.openInputStream(uri)?.use { ins ->
            ins.readBytes().toString(Charsets.UTF_8)
        } ?: throw IllegalStateException("Не удалось открыть файл")

        val root = try {
            JSONObject(text)
        } catch (e: Exception) {
            throw IllegalArgumentException("Файл не является корректным JSON-бэкапом")
        }

        val version = root.optInt("version", -1)
        if (version != VERSION) {
            throw IllegalArgumentException("Неподдерживаемая версия бэкапа: $version")
        }
        val exportDate = root.optString("exportDate", "")

        val items = mutableListOf<Item>()
        val itemsArr = root.optJSONArray("items") ?: JSONArray()
        for (i in 0 until itemsArr.length()) {
            val o = itemsArr.getJSONObject(i)
            val inv = InvNormalizer.normalize(o.optString("invNumber"))
            if (inv.isEmpty()) continue
            items.add(
                Item(
                    invNumber = inv,
                    name = o.optString("name"),
                    unit = o.optString("unit", "шт"),
                    expectedQty = o.optInt("expectedQty", 1).coerceAtLeast(1)
                )
            )
        }

        val events = mutableListOf<ScanEvent>()
        val eventsArr = root.optJSONArray("scanEvents") ?: JSONArray()
        for (i in 0 until eventsArr.length()) {
            val o = eventsArr.getJSONObject(i)
            val inv = InvNormalizer.normalize(o.optString("invNumber"))
            if (inv.isEmpty()) continue
            val type = o.optString("type", "NORMAL")
            events.add(
                ScanEvent(
                    invNumber = inv,
                    timestamp = o.optLong("timestamp", System.currentTimeMillis()),
                    type = if (type == "OVERAGE") "OVERAGE" else "NORMAL",
                    source = when (o.optString("source", "MANUAL")) {
                        "CAMERA" -> "CAMERA"
                        else -> "MANUAL"
                    }
                )
            )
        }

        return BackupData(version, exportDate, items, events)
    }
}
