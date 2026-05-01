package com.nexusremote.app

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.BitmapFactory
import android.os.Build
import android.os.Bundle
import android.util.Base64
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nexusremote.app.ui.theme.NexusRemoteTheme
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import java.io.OutputStreamWriter
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.URL
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.Executors
import kotlinx.coroutines.delay
import org.json.JSONArray
import org.json.JSONObject

class MainActivity : ComponentActivity() {
    private val commandExecutor = Executors.newSingleThreadExecutor()
    private val pollingExecutor = Executors.newFixedThreadPool(2)
    private val contextInFlight = AtomicBoolean(false)
    private val snapshotInFlight = AtomicBoolean(false)
    private val prefs by lazy { getSharedPreferences("nexus_remote", Context.MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        createNotificationChannel()
        requestNotificationPermission()
        val savedSettings = loadConnectionSettings()
        setContent {
            NexusRemoteTheme(dynamicColor = false) {
                var showSplash by remember { mutableStateOf(true) }
                var status by remember { mutableStateOf("Введите IP ПК и проверьте связь") }
                var pcContext by remember { mutableStateOf(PcContext()) }
                var pcSnapshot by remember { mutableStateOf(PcSnapshot()) }

                LaunchedEffect(Unit) {
                    delay(3000)
                    showSplash = false
                }

                AnimatedContent(
                    targetState = showSplash,
                    transitionSpec = { fadeIn(tween(260)) togetherWith fadeOut(tween(220)) },
                    label = "startup"
                ) { splash ->
                    if (splash) {
                        SplashScreen()
                    } else {
                        RemoteApp(
                            status = status,
                            pcContext = pcContext,
                            pcSnapshot = pcSnapshot,
                            initialSettings = savedSettings,
                            onSettingsChange = ::saveConnectionSettings,
                            onServerUnavailable = ::showServerUnavailableNotification,
                            onDiscoverServer = { onResult ->
                                discoverServer { result ->
                                    runOnUiThread { onResult(result) }
                                }
                            },
                            onPairingRequest = { host, port, pairingToken, onResult ->
                                status = "Ожидаю подтверждение на ПК"
                                pairWithServer(host, port, pairingToken) { settings, message ->
                                    runOnUiThread {
                                        status = message
                                        onResult(settings)
                                    }
                                }
                            },
                            onCheckUpdates = { onResult ->
                                checkForAndroidUpdates { message ->
                                    runOnUiThread { onResult(message) }
                                }
                            },
                            onRefreshContext = { host, port ->
                                fetchContext(host, port, savedToken = prefs.getString("token", "").orEmpty()) { context ->
                                    runOnUiThread { pcContext = context }
                                }
                            },
                            onRefreshSnapshot = { host, port ->
                                fetchSnapshot(host, port, savedToken = prefs.getString("token", "").orEmpty()) { snapshot ->
                                    runOnUiThread {
                                        if (snapshot != null) {
                                            pcSnapshot = snapshot
                                            if (status.startsWith("Нет связи") || status.startsWith("Ошибка")) {
                                                status = "Готово: snapshot"
                                            }
                                        } else {
                                            status = "ПК недоступен. Откройте Nexus Remote PC и отсканируйте QR."
                                        }
                                    }
                                }
                            },
                            onCommand = { host, port, token, type, payload ->
                                status = "Отправляю: $type"
                                sendCommand(host, port, token, type, payload) { result ->
                                    runOnUiThread { status = result }
                                }
                            }
                        )
                    }
                }
            }
        }
    }

    override fun onDestroy() {
        commandExecutor.shutdownNow()
        pollingExecutor.shutdownNow()
        super.onDestroy()
    }

    private fun loadConnectionSettings(): ConnectionSettings {
        return ConnectionSettings(
            host = prefs.getString("host", "").orEmpty(),
            port = prefs.getString("port", "8765").orEmpty(),
            token = prefs.getString("token", "").orEmpty()
        )
    }

    private fun saveConnectionSettings(settings: ConnectionSettings) {
        prefs.edit()
            .putString("host", settings.host)
            .putString("port", settings.port)
            .putString("token", settings.token)
            .apply()
    }

    private fun checkForAndroidUpdates(onResult: (String) -> Unit) {
        commandExecutor.execute {
            try {
                val url = URL("https://api.github.com/repos/Deenfoool/nexus-remote-PC-APK/releases/latest")
                val connection = (url.openConnection() as HttpURLConnection).apply {
                    requestMethod = "GET"
                    connectTimeout = 2500
                    readTimeout = 4000
                    setRequestProperty("Accept", "application/vnd.github+json")
                    setRequestProperty("User-Agent", "NexusRemote-Android")
                }
                val responseCode = connection.responseCode
                if (responseCode == 404) {
                    connection.disconnect()
                    onResult("Публичный релиз ещё не опубликован")
                    return@execute
                }
                if (responseCode !in 200..299) {
                    connection.disconnect()
                    onResult("Не удалось проверить обновления")
                    return@execute
                }

                val responseText = connection.inputStream.bufferedReader().use { it.readText() }
                connection.disconnect()
                val root = JSONObject(responseText)
                val latestTag = root.optString("tag_name").trim().removePrefix("v")
                val currentTag = BuildConfig.VERSION_NAME.trim().removePrefix("v")
                val latestParts = latestTag.split('.').mapNotNull { it.toIntOrNull() }
                val currentParts = currentTag.split('.').mapNotNull { it.toIntOrNull() }
                val hasUpdate = compareVersionParts(latestParts, currentParts) > 0
                onResult(
                    if (hasUpdate) "Доступна версия $latestTag"
                    else "Установлена актуальная версия $currentTag"
                )
            } catch (error: Exception) {
                onResult("Ошибка обновлений: ${error.message ?: error.javaClass.simpleName}")
            }
        }
    }

    private fun compareVersionParts(left: List<Int>, right: List<Int>): Int {
        val size = maxOf(left.size, right.size)
        repeat(size) { index ->
            val l = left.getOrElse(index) { 0 }
            val r = right.getOrElse(index) { 0 }
            if (l != r) return l.compareTo(r)
        }
        return 0
    }

    private fun pairWithServer(
        host: String,
        port: String,
        pairingToken: String,
        onResult: (ConnectionSettings?, String) -> Unit
    ) {
        commandExecutor.execute {
            try {
                val url = URL("http://${host.trim()}:${port.trim()}/pair")
                val body = JSONObject()
                    .put("pairingToken", pairingToken)
                    .put("deviceName", "${Build.MANUFACTURER} ${Build.MODEL}".trim())
                    .toString()

                val connection = (url.openConnection() as HttpURLConnection).apply {
                    requestMethod = "POST"
                    connectTimeout = 2500
                    readTimeout = 30000
                    doOutput = true
                    setRequestProperty("Content-Type", "application/json")
                }

                OutputStreamWriter(connection.outputStream).use { it.write(body) }
                val responseCode = connection.responseCode
                val responseText = runCatching {
                    val stream = if (responseCode in 200..299) connection.inputStream else connection.errorStream
                    stream?.bufferedReader()?.use { it.readText() }.orEmpty()
                }.getOrDefault("")
                connection.disconnect()

                if (responseCode !in 200..299) {
                    onResult(null, "Сопряжение отклонено или QR устарел")
                    return@execute
                }

                val device = JSONObject(responseText).optJSONObject("device") ?: JSONObject()
                val deviceToken = device.optString("token")
                if (deviceToken.isBlank()) {
                    onResult(null, "ПК не выдал device token")
                    return@execute
                }

                onResult(
                    ConnectionSettings(host = host, port = port, token = deviceToken),
                    "Готово: устройство сопряжено"
                )
            } catch (error: Exception) {
                onResult(null, "Ошибка сопряжения: ${error.message ?: error.javaClass.simpleName}")
            }
        }
    }

    private fun discoverServer(onResult: (ConnectionSettings?) -> Unit) {
        pollingExecutor.execute {
            try {
                DatagramSocket().use { socket ->
                    socket.broadcast = true
                    socket.soTimeout = 1600
                    val request = "NEXUS_REMOTE_DISCOVER".toByteArray()
                    val packet = DatagramPacket(
                        request,
                        request.size,
                        InetAddress.getByName("255.255.255.255"),
                        8766
                    )
                    socket.send(packet)
                    val buffer = ByteArray(2048)
                    val response = DatagramPacket(buffer, buffer.size)
                    socket.receive(response)
                    val json = JSONObject(String(response.data, 0, response.length))
                    onResult(
                        ConnectionSettings(
                            host = json.optString("host", response.address.hostAddress ?: ""),
                            port = json.optInt("port", 8765).toString(),
                            token = prefs.getString("token", "").orEmpty()
                        )
                    )
                }
            } catch (_: Exception) {
                onResult(null)
            }
        }
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                "nexus_connection",
                "Nexus Remote",
                NotificationManager.IMPORTANCE_DEFAULT
            )
            getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
        }
    }

    private fun requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 42)
        }
    }

    private fun showServerUnavailableNotification() {
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) return

        val intent = Intent(this, MainActivity::class.java)
        val pendingIntent = PendingIntent.getActivity(
            this,
            0,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val notification = NotificationCompat.Builder(this, "nexus_connection")
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("Nexus Remote")
            .setContentText("ПК недоступен. Откройте Nexus Remote PC и проверьте Wi-Fi.")
            .setContentIntent(pendingIntent)
            .setAutoCancel(true)
            .build()
        (getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).notify(1001, notification)
    }

    private fun sendCommand(
        host: String,
        port: String,
        token: String,
        type: String,
        payload: JSONObject = JSONObject(),
        onResult: (String) -> Unit
    ) {
        commandExecutor.execute {
            try {
                val url = URL("http://${host.trim()}:${port.trim()}/command")
                val body = JSONObject()
                    .put("deviceToken", token)
                    .put("token", token)
                    .put("type", type)
                    .put("payload", payload)
                    .toString()

                val connection = (url.openConnection() as HttpURLConnection).apply {
                    requestMethod = "POST"
                    connectTimeout = 1200
                    readTimeout = 1200
                    doOutput = true
                    setRequestProperty("Content-Type", "application/json")
                    setRequestProperty("X-Nexus-Device-Token", token)
                }

                OutputStreamWriter(connection.outputStream).use { it.write(body) }
                val responseCode = connection.responseCode
                val responseText = runCatching {
                    val stream = if (responseCode in 200..299) connection.inputStream else connection.errorStream
                    stream?.bufferedReader()?.use { it.readText() }.orEmpty()
                }.getOrDefault("")
                val message = runCatching { JSONObject(responseText).optString("message") }.getOrDefault("")
                val ok = responseCode in 200..299
                onResult(
                    when {
                        ok && message.isNotBlank() -> message
                        ok -> "Готово: $type"
                        responseText.isNotBlank() -> "Ошибка: $responseText"
                        else -> "Ошибка сервера: $responseCode"
                    }
                )
                connection.disconnect()
            } catch (error: Exception) {
                onResult("Нет связи: ${error.message ?: error.javaClass.simpleName}")
            }
        }
    }

    private fun fetchContext(host: String, port: String, savedToken: String, onResult: (PcContext) -> Unit) {
        pollingExecutor.execute {
            if (!contextInFlight.compareAndSet(false, true)) return@execute
            try {
                val url = URL("http://${host.trim()}:${port.trim()}/context")
                val connection = (url.openConnection() as HttpURLConnection).apply {
                    requestMethod = "GET"
                    connectTimeout = 900
                    readTimeout = 900
                    setRequestProperty("X-Nexus-Device-Token", savedToken)
                }
                val responseText = connection.inputStream.bufferedReader().use { it.readText() }
                val context = JSONObject(responseText).optJSONObject("context") ?: JSONObject()
                onResult(
                    PcContext(
                        activeTitle = context.optString("active_title"),
                        activeProcess = context.optString("active_process"),
                        suggestedSection = context.optString("suggested_section"),
                        suggestedLabel = context.optString("suggested_label")
                    )
                )
                connection.disconnect()
            } catch (_: Exception) {
                onResult(PcContext())
            } finally {
                contextInFlight.set(false)
            }
        }
    }

    private fun fetchSnapshot(host: String, port: String, savedToken: String, onResult: (PcSnapshot?) -> Unit) {
        pollingExecutor.execute {
            if (!snapshotInFlight.compareAndSet(false, true)) return@execute
            try {
                val url = URL("http://${host.trim()}:${port.trim()}/snapshot")
                val connection = (url.openConnection() as HttpURLConnection).apply {
                    requestMethod = "GET"
                    connectTimeout = 2500
                    readTimeout = 8500
                    setRequestProperty("X-Nexus-Device-Token", savedToken)
                }
                val responseText = connection.inputStream.bufferedReader().use { it.readText() }
                val snapshot = JSONObject(responseText).optJSONObject("snapshot") ?: JSONObject()
                onResult(parseSnapshot(snapshot))
                connection.disconnect()
            } catch (_: Exception) {
                onResult(null)
            } finally {
                snapshotInFlight.set(false)
            }
        }
    }

    private fun parseSnapshot(snapshot: JSONObject): PcSnapshot {
        val cpu = snapshot.optJSONObject("cpu") ?: JSONObject()
        val gpu = snapshot.optJSONObject("gpu") ?: JSONObject()
        val ram = snapshot.optJSONObject("ram") ?: JSONObject()
        val disk = snapshot.optJSONObject("disk") ?: JSONObject()
        val storage = snapshot.optJSONObject("storage") ?: JSONObject()
        val fans = snapshot.optJSONObject("fans") ?: JSONObject()
        val network = snapshot.optJSONObject("network") ?: JSONObject()
        val system = snapshot.optJSONObject("system") ?: JSONObject()
        val media = snapshot.optJSONObject("media") ?: JSONObject()
        val programs = snapshot.optJSONArray("programs") ?: JSONArray()
        val mediaSources = media.optJSONArray("sources") ?: JSONArray()
        val activeSourceId = media.optString("active_source_id")
        val activeSource = findActiveMediaSource(mediaSources, activeSourceId)
        val activeMediaTitle = activeSource?.optString("title").orEmpty()
        val activeMediaArtist = activeSource?.optString("artist").orEmpty()
        val activeMediaApp = activeSource?.optString("app_name").orEmpty()
        val activeMediaArtwork = activeSource?.optString("artwork_base64").orEmpty()
        val mediaSubtitle = listOf(activeMediaArtist, activeMediaApp)
            .filter { it.isNotBlank() }
            .joinToString(" • ")
        val parsedMediaSources = List(mediaSources.length()) { index ->
            val item = mediaSources.optJSONObject(index) ?: JSONObject()
            val capabilities = item.optJSONObject("capabilities") ?: JSONObject()
            MediaSourceUi(
                sourceId = item.optString("source_id"),
                sourceType = item.optString("source_type"),
                appName = item.optString("app_name"),
                site = item.optString("site"),
                title = item.optString("title"),
                artist = item.optString("artist"),
                tabTitle = item.optString("tab_title"),
                artworkBase64 = item.optString("artwork_base64"),
                mediaKind = item.optString("media_kind"),
                positionMs = item.optLong("position_ms"),
                durationMs = item.optLong("duration_ms"),
                isPlaying = item.optBoolean("is_playing"),
                canTogglePlayPause = capabilities.optBoolean("can_toggle_play_pause", true),
                canNext = capabilities.optBoolean("can_next"),
                canPrevious = capabilities.optBoolean("can_previous"),
                canSeek = capabilities.optBoolean("can_seek")
            )
        }.filter { it.sourceId.isNotBlank() }

        return PcSnapshot(
            hostname = system.optString("hostname", "Ждём данные"),
            osName = system.optString("os", "Windows"),
            uptime = system.optString("uptime", "Ждём"),
            processCount = system.optInt("process_count", system.optInt("processes", 0)),
            powerStatus = system.optString("power_status", system.optString("power", "Нормально")),
            lastAction = system.optString("last_action", "Нет"),
            systemVolume = if (system.isNull("volume")) 70 else system.optInt("volume", 70),
            screenBrightness = if (system.isNull("brightness")) null else system.optInt("brightness"),
            cpuLoad = cpu.optInt("load", 0),
            cpuTemp = if (cpu.isNull("temperature")) null else cpu.optInt("temperature"),
            gpuLoad = if (gpu.isNull("load")) null else gpu.optInt("load"),
            gpuTemp = if (gpu.isNull("temperature")) null else gpu.optInt("temperature"),
            ramPercent = ram.optInt("percent", 0),
            ramUsedGb = ram.optDouble("used_gb", 0.0),
            ramTotalGb = ram.optDouble("total_gb", 0.0),
            diskPercent = disk.optInt("percent", 0),
            diskUsedGb = disk.optInt("used_gb", 0),
            diskTotalGb = disk.optInt("total_gb", 0),
            storageActivity = if (storage.isNull("activity")) null else storage.optInt("activity"),
            storageTemp = if (storage.isNull("temperature")) null else storage.optInt("temperature"),
            fanRpm = if (fans.isNull("rpm")) null else fans.optInt("rpm"),
            networkTotalMbps = network.optInt("total_mbps", 0),
            networkDownMbps = network.optInt("down_mbps", 0),
            networkUpMbps = network.optInt("up_mbps", 0),
            mediaSourceId = activeSourceId,
            mediaTitle = activeMediaTitle.ifBlank { media.optString("title") },
            mediaProcess = mediaSubtitle.ifBlank { activeMediaApp.ifBlank { media.optString("process") } },
            mediaArtworkBase64 = activeMediaArtwork,
            mediaPositionMs = activeSource?.optLong("position_ms") ?: media.optLong("position"),
            mediaDurationMs = activeSource?.optLong("duration_ms") ?: media.optLong("duration"),
            mediaIsPlaying = activeSource?.optBoolean("is_playing") ?: false,
            mediaSources = parsedMediaSources,
            programs = List(programs.length()) { index ->
                val item = programs.optJSONObject(index) ?: JSONObject()
                ProgramStatus(
                    name = item.optString("name"),
                    command = item.optString("command"),
                    running = item.optBoolean("running"),
                    iconBase64 = item.optString("icon")
                )
            }
        )
    }
}

data class PcContext(
    val activeTitle: String = "",
    val activeProcess: String = "",
    val suggestedSection: String = "",
    val suggestedLabel: String = ""
)

data class ConnectionSettings(
    val host: String = "",
    val port: String = "8765",
    val token: String = ""
)

data class PairingQr(
    val host: String,
    val port: String,
    val pairingToken: String
)

data class ProgramStatus(
    val name: String = "",
    val command: String = "",
    val running: Boolean = false,
    val iconBase64: String = ""
)

data class PcSnapshot(
    val hostname: String = "Ждём данные",
    val osName: String = "Windows",
    val uptime: String = "Ждём",
    val processCount: Int = 0,
    val powerStatus: String = "Нормально",
    val lastAction: String = "Нет",
    val systemVolume: Int = 70,
    val screenBrightness: Int? = null,
    val cpuLoad: Int = 0,
    val cpuTemp: Int? = null,
    val gpuLoad: Int? = null,
    val gpuTemp: Int? = null,
    val ramPercent: Int = 0,
    val ramUsedGb: Double = 0.0,
    val ramTotalGb: Double = 0.0,
    val diskPercent: Int = 0,
    val diskUsedGb: Int = 0,
    val diskTotalGb: Int = 0,
    val storageActivity: Int? = null,
    val storageTemp: Int? = null,
    val fanRpm: Int? = null,
    val networkTotalMbps: Int = 0,
    val networkDownMbps: Int = 0,
    val networkUpMbps: Int = 0,
    val mediaSourceId: String = "",
    val mediaTitle: String = "",
    val mediaProcess: String = "",
    val mediaArtworkBase64: String = "",
    val mediaPositionMs: Long = 0,
    val mediaDurationMs: Long = 0,
    val mediaIsPlaying: Boolean = false,
    val mediaSources: List<MediaSourceUi> = emptyList(),
    val programs: List<ProgramStatus> = emptyList()
)

data class MediaSourceUi(
    val sourceId: String = "",
    val sourceType: String = "",
    val appName: String = "",
    val site: String = "",
    val title: String = "",
    val artist: String = "",
    val tabTitle: String = "",
    val artworkBase64: String = "",
    val mediaKind: String = "unknown",
    val positionMs: Long = 0,
    val durationMs: Long = 0,
    val isPlaying: Boolean = false,
    val canTogglePlayPause: Boolean = true,
    val canNext: Boolean = false,
    val canPrevious: Boolean = false,
    val canSeek: Boolean = false
) {
    val displayTitle: String
        get() = title.ifBlank { tabTitle.ifBlank { "Медиа-источник" } }

    val displaySubtitle: String
        get() = listOf(artist, appName, site)
            .filter { it.isNotBlank() }
            .joinToString(" • ")
            .ifBlank { "Источник без metadata" }
}

private fun findActiveMediaSource(sources: JSONArray, activeSourceId: String): JSONObject? {
    if (sources.length() == 0) return null
    if (activeSourceId.isNotBlank()) {
        repeat(sources.length()) { index ->
            val item = sources.optJSONObject(index) ?: return@repeat
            if (item.optString("source_id") == activeSourceId) {
                return item
            }
        }
    }
    return sources.optJSONObject(0)
}

private enum class MainTab(val title: String, val header: String, val icon: NexusIcon) {
    Monitor("Мониторинг", "Мой ПК", NexusIcon.Monitor),
    Player("Плеер", "Видеоплеер", NexusIcon.Play),
    Programs("Программы", "Программы на ПК", NexusIcon.Grid),
    System("Система", "Питание и система", NexusIcon.Power)
}

private enum class NexusIcon(val resId: Int) {
    Battery(R.drawable.ic_nx_battery),
    Chip(R.drawable.ic_nx_chip),
    Close(R.drawable.ic_nx_close),
    Computer(R.drawable.ic_nx_monitor),
    Forward(R.drawable.ic_nx_next),
    Fullscreen(R.drawable.ic_nx_fullscreen),
    Grid(R.drawable.ic_nx_grid),
    Lock(R.drawable.ic_nx_lock),
    Monitor(R.drawable.ic_nx_monitor),
    More(R.drawable.ic_nx_more),
    Moon(R.drawable.ic_nx_moon),
    Network(R.drawable.ic_nx_wave),
    Pause(R.drawable.ic_nx_pause),
    Play(R.drawable.ic_nx_play),
    Power(R.drawable.ic_nx_power),
    Refresh(R.drawable.ic_nx_refresh),
    Rewind(R.drawable.ic_nx_back),
    SkipNext(R.drawable.ic_nx_next),
    SkipPrevious(R.drawable.ic_nx_back),
    Storage(R.drawable.ic_nx_storage),
    Subtitles(R.drawable.ic_nx_subtitles),
    Sun(R.drawable.ic_nx_sun),
    Task(R.drawable.ic_nx_grid),
    Volume(R.drawable.ic_nx_volume),
    VolumeOff(R.drawable.ic_nx_volume_off),
    Wave(R.drawable.ic_nx_wave)
}

private data class ProgramItem(
    val name: String,
    val command: String,
    val color: Color,
    val glyph: String,
    val logo: String,
    val iconBase64: String = ""
)

private val BgTop = Color(0xFF09111D)
private val BgBottom = Color(0xFF020914)
private val CardBg = Color(0xE6111C2B)
private val CardStroke = Color(0x263C526B)
private val TextMain = Color(0xFFF8FAFC)
private val TextMuted = Color(0xFF98A2B3)
private val Cyan = Color(0xFF28B8FF)
private val Teal = Color(0xFF2DE7D2)
private val Track = Color(0xFF253145)
private val Green = Color(0xFF25E6B4)
private val Danger = Color(0xFFFB7185)

private val ProgramColors = listOf(
    Color(0xFF1A9FFF),
    Color(0xFF2DE7D2),
    Color(0xFF8B5CF6),
    Color(0xFF22C55E),
    Color(0xFFF59E0B),
    Color(0xFFEF4444),
    Color(0xFF06B6D4)
)

@Composable
private fun SplashScreen() {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(BgTop, BgBottom))),
        contentAlignment = Alignment.Center
    ) {
        Image(
            painter = painterResource(id = R.drawable.splash_logo),
            contentDescription = "Nexus Remote",
            contentScale = ContentScale.Crop,
            modifier = Modifier.fillMaxSize()
        )
    }
}

private fun parsePairingQr(contents: String?): PairingQr? {
    if (contents.isNullOrBlank()) return null
    return runCatching {
        val json = JSONObject(contents)
        if (json.optString("app") != "nexus-remote") return null
        PairingQr(
            host = json.optString("host"),
            port = json.optInt("port", 8765).toString(),
            pairingToken = json.optString("pairingToken", json.optString("token"))
        )
    }.getOrNull()
}

@Composable
fun RemoteApp(
    status: String,
    pcContext: PcContext,
    pcSnapshot: PcSnapshot,
    initialSettings: ConnectionSettings = ConnectionSettings(),
    onSettingsChange: (ConnectionSettings) -> Unit = {},
    onServerUnavailable: () -> Unit = {},
    onDiscoverServer: ((ConnectionSettings?) -> Unit) -> Unit = { callback -> callback(null) },
    onPairingRequest: (host: String, port: String, pairingToken: String, onResult: (ConnectionSettings?) -> Unit) -> Unit = { _, _, _, callback -> callback(null) },
    onCheckUpdates: ((String) -> Unit) -> Unit = { callback -> callback("Проверка обновлений недоступна") },
    onRefreshContext: (host: String, port: String) -> Unit,
    onRefreshSnapshot: (host: String, port: String) -> Unit,
    onCommand: (host: String, port: String, token: String, type: String, payload: JSONObject) -> Unit
) {
    var host by remember { mutableStateOf(initialSettings.host) }
    var port by remember { mutableStateOf(initialSettings.port) }
    var token by remember { mutableStateOf(initialSettings.token) }
    var tab by remember { mutableStateOf(MainTab.Monitor) }
    var showConnection by remember { mutableStateOf(false) }
    var showOnboarding by remember { mutableStateOf(initialSettings.token.isBlank()) }
    var unavailableNotified by remember { mutableStateOf(false) }
    var updateStatus by remember { mutableStateOf("Проверка обновлений ещё не запускалась") }
    val versionText = remember { "Версия ${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})" }
    val qrScanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        parsePairingQr(result.contents)?.let { qr ->
            host = qr.host
            port = qr.port
            onPairingRequest(qr.host, qr.port, qr.pairingToken) { settings ->
                if (settings != null) {
                    host = settings.host
                    port = settings.port
                    token = settings.token
                }
            }
        }
    }

    val connected = status.startsWith("Готово") ||
        status.contains("pong", ignoreCase = true) ||
        status.startsWith("Яркость")

    fun send(type: String, payload: JSONObject = JSONObject()) {
        if (host.isNotBlank() && port.isNotBlank() && token.isNotBlank()) {
            onCommand(host, port, token, type, payload)
        } else {
            showConnection = true
        }
    }

    LaunchedEffect(host, port) {
        while (true) {
            if (host.isNotBlank() && port.isNotBlank()) {
                onRefreshContext(host, port)
                onRefreshSnapshot(host, port)
            }
            delay(5000)
        }
    }

    LaunchedEffect(connected) {
        if (!connected && status.startsWith("Нет связи")) {
            showConnection = true
            if (!unavailableNotified) {
                onServerUnavailable()
                unavailableNotified = true
            }
        }
        if (connected) unavailableNotified = false
    }

    LaunchedEffect(host, port, token) {
        onSettingsChange(ConnectionSettings(host, port, token))
    }

    Scaffold(
        containerColor = BgBottom,
        bottomBar = {
            BottomNav(selected = tab, onSelect = { tab = it })
        }
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .background(Brush.verticalGradient(listOf(BgTop, BgBottom)))
                .padding(innerPadding)
                .padding(horizontal = 16.dp)
                .padding(top = 18.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp)
        ) {
            Header(
                title = tab.header,
                connected = connected,
                status = status,
                onConnectionClick = { showConnection = true }
            )

            ContextHint(context = pcContext, tab = tab, onOpen = { tab = it })

            if (showOnboarding && !connected) {
                OnboardingCard(
                    onFindPc = {
                        onDiscoverServer { discovered ->
                            if (discovered != null) {
                                host = discovered.host
                                port = discovered.port
                            }
                        }
                    },
                    onScanQr = {
                        qrScanner.launch(
                            ScanOptions()
                                .setPrompt("Сканируйте QR в окне Nexus Remote PC")
                                .setBeepEnabled(false)
                                .setOrientationLocked(false)
                        )
                    },
                    onOpenSettings = { showConnection = true }
                )
            }

            AnimatedContent(
                targetState = tab,
                transitionSpec = { fadeIn(tween(180)).togetherWith(fadeOut(tween(140))) },
                label = "main-tab",
                modifier = Modifier.weight(1f)
            ) { current ->
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    when (current) {
                        MainTab.Monitor -> MonitorScreen(pcSnapshot)
                        MainTab.Player -> PlayerScreen(snapshot = pcSnapshot, onCommand = ::send)
                        MainTab.Programs -> ProgramsScreen(snapshot = pcSnapshot, onCommand = ::send)
                        MainTab.System -> SystemScreen(snapshot = pcSnapshot, onCommand = ::send)
                    }
                    Spacer(Modifier.height(12.dp))
                }
            }
        }
    }

    if (showConnection) {
        ConnectionDialog(
            connected = connected,
            host = host,
            port = port,
            token = token,
            status = status,
            onHostChange = { host = it },
            onPortChange = { port = it },
            onTokenChange = { token = it },
            onPing = { send("ping") },
            onDiscover = {
                onDiscoverServer { discovered ->
                    if (discovered != null) {
                        host = discovered.host
                        port = discovered.port
                    }
                }
            },
            onScanQr = {
                qrScanner.launch(
                    ScanOptions()
                        .setPrompt("Наведите камеру на QR в Nexus Remote PC Server")
                        .setBeepEnabled(false)
                        .setOrientationLocked(false)
                )
            },
            onOpenPrograms = { send("programs_gui_open") },
            onRefreshPrograms = {
                send("programs_refresh")
                onRefreshSnapshot(host, port)
            },
            versionText = versionText,
            updateStatus = updateStatus,
            onCheckUpdates = {
                onCheckUpdates { message -> updateStatus = message }
            },
            onDismiss = {
                if (connected) {
                    showOnboarding = false
                    showConnection = false
                }
            }
        )
    }
}

@Composable
private fun OnboardingCard(
    onFindPc: () -> Unit,
    onScanQr: () -> Unit,
    onOpenSettings: () -> Unit
) {
    GlowCard {
        Text("Первое подключение", color = TextMain, style = androidx.compose.material3.MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        OnboardingStep("1", "Установите и откройте Nexus Remote PC на компьютере.")
        OnboardingStep("2", "Нажмите “Найти ПК”, если телефон и ПК в одной Wi-Fi сети.")
        OnboardingStep("3", "Сканируйте QR в окне PC-приложения и подтвердите доступ на ПК.")
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
            Button(
                onClick = onFindPc,
                modifier = Modifier.weight(1f).height(46.dp),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF162235))
            ) {
                Text("Найти ПК", maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Bold)
            }
            Button(
                onClick = onScanQr,
                modifier = Modifier.weight(1f).height(46.dp),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Cyan)
            ) {
                Text("Сканировать QR", maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Bold)
            }
        }
        TextButton(onClick = onOpenSettings, modifier = Modifier.align(Alignment.End)) {
            Text("Настройки вручную", color = TextMuted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun OnboardingStep(number: String, text: String) {
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
        Box(
            modifier = Modifier
                .size(24.dp)
                .clip(CircleShape)
                .background(Color(0xFF18304A)),
            contentAlignment = Alignment.Center
        ) {
            Text(number, color = Cyan, fontWeight = FontWeight.Bold, style = androidx.compose.material3.MaterialTheme.typography.bodySmall)
        }
        Text(text, color = TextMuted, modifier = Modifier.weight(1f), lineHeight = 20.sp)
    }
}

@Composable
private fun Header(title: String, connected: Boolean, status: String, onConnectionClick: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.Top
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.weight(1f)) {
            Text(title, color = TextMain, style = androidx.compose.material3.MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(12.dp).clip(CircleShape).background(if (connected) Green else Color(0xFF64748B)))
                Text(if (connected) "Подключено" else "Нет подключения", color = if (connected) Green else TextMuted, fontWeight = FontWeight.Medium)
            }
            if (!connected) {
                Text(status, color = Danger, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
        IconButton(onClick = onConnectionClick) {
            Icon(
                icon = NexusIcon.More,
                contentDescription = "Подключение",
                tint = TextMuted
            )
        }
    }
}

@Composable
private fun ContextHint(context: PcContext, tab: MainTab, onOpen: (MainTab) -> Unit) {
    val target = when (context.suggestedSection) {
        "Browser" -> MainTab.Programs
        "Media" -> MainTab.Player
        "Apps", "Keys" -> MainTab.Programs
        else -> null
    }
    val process = context.activeProcess.removeSuffix(".exe")
    if (target == null || target == tab || process.isBlank()) return

    GlowCard {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Column(Modifier.weight(1f)) {
                Text("На ПК открыт $process", color = TextMain, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("Предложить: ${target.title}", color = TextMuted)
            }
            CapsuleButton("Открыть", filled = true) { onOpen(target) }
        }
    }
}

@Composable
private fun MonitorScreen(snapshot: PcSnapshot) {
    var cpuHistory by remember { mutableStateOf(seedHistory(snapshot.cpuLoad)) }
    var gpuHistory by remember { mutableStateOf(seedHistory(snapshot.gpuLoad ?: 0)) }
    var ramHistory by remember { mutableStateOf(seedHistory(snapshot.ramPercent)) }
    var diskHistory by remember { mutableStateOf(seedHistory(snapshot.diskPercent)) }
    var storageHistory by remember { mutableStateOf(seedHistory(snapshot.storageActivity ?: 0)) }
    var fanHistory by remember { mutableStateOf(seedHistory((snapshot.fanRpm ?: 0) / 30)) }
    var networkHistory by remember { mutableStateOf(seedHistory(snapshot.networkTotalMbps)) }

    LaunchedEffect(snapshot.cpuLoad) {
        cpuHistory = appendHistory(cpuHistory, snapshot.cpuLoad)
    }
    LaunchedEffect(snapshot.gpuLoad) {
        snapshot.gpuLoad?.let { gpuHistory = appendHistory(gpuHistory, it) }
    }
    LaunchedEffect(snapshot.ramPercent) {
        ramHistory = appendHistory(ramHistory, snapshot.ramPercent)
    }
    LaunchedEffect(snapshot.diskPercent) {
        diskHistory = appendHistory(diskHistory, snapshot.diskPercent)
    }
    LaunchedEffect(snapshot.storageActivity) {
        snapshot.storageActivity?.let { storageHistory = appendHistory(storageHistory, it) }
    }
    LaunchedEffect(snapshot.fanRpm) {
        snapshot.fanRpm?.let { fanHistory = appendHistory(fanHistory, it / 30) }
    }
    LaunchedEffect(snapshot.networkTotalMbps) {
        networkHistory = appendHistory(networkHistory, snapshot.networkTotalMbps)
    }

    val ramUsed = if (snapshot.ramUsedGb > 0) formatGb(snapshot.ramUsedGb) else "Ждём"
    val ramTotal = if (snapshot.ramTotalGb > 0) "/ ${formatGb(snapshot.ramTotalGb)} GB" else "данные"
    val diskDetail = if (snapshot.diskTotalGb > 0) "${snapshot.diskUsedGb} GB / ${snapshot.diskTotalGb} GB" else "Ждём данные"
    val gpuLoad = snapshot.gpuLoad
    val storageActivity = snapshot.storageActivity

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            MetricCard(NexusIcon.Chip, "CPU", snapshot.cpuLoad, Cyan, Modifier.weight(1f), history = cpuHistory)
            if (gpuLoad != null) {
                MetricCard(NexusIcon.Chip, "GPU", gpuLoad, Teal, Modifier.weight(1f), history = gpuHistory)
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            UsageCard(NexusIcon.Grid, "RAM", ramUsed, ramTotal, "${snapshot.ramPercent}%", Modifier.weight(1f), progress = snapshot.ramPercent / 100f)
            UsageCard(NexusIcon.Storage, "Диск C:", snapshot.diskPercent.toString(), "%", diskDetail, Modifier.weight(1f), progress = snapshot.diskPercent / 100f)
        }
        if (storageActivity != null || snapshot.storageTemp != null) {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                if (storageActivity != null) {
                    SparkCard(NexusIcon.Storage, "Диски", "Активность", "$storageActivity%", Teal, Modifier.weight(1f), history = storageHistory)
                }
                if (snapshot.storageTemp != null) {
                    RingOnlyCard(NexusIcon.Storage, "SSD / HDD", snapshot.storageTemp.toString(), "°C", Modifier.weight(1f))
                } else if (storageActivity != null) {
                    SparkCard(NexusIcon.Storage, "SSD / HDD", "Активность", "$storageActivity%", Teal, Modifier.weight(1f), history = storageHistory)
                }
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            if (snapshot.fanRpm != null && snapshot.fanRpm > 0) {
                SparkCard(NexusIcon.Wave, "Вентиляторы", "Скорость", "${snapshot.fanRpm} RPM", Cyan, Modifier.weight(1f), history = fanHistory)
            }
            NetworkCard(snapshot, Modifier.weight(1f), history = networkHistory)
        }
    }
}

@Composable
private fun PlayerScreen(snapshot: PcSnapshot, onCommand: (String, JSONObject) -> Unit) {
    var volume by remember { mutableFloatStateOf(snapshot.systemVolume.toFloat()) }
    var selectedSourceId by remember { mutableStateOf(snapshot.mediaSourceId) }
    var seekValue by remember { mutableFloatStateOf(snapshot.mediaPositionMs.toFloat()) }
    var isSeeking by remember { mutableStateOf(false) }
    val selectedSource = snapshot.mediaSources.firstOrNull { it.sourceId == selectedSourceId }
        ?: snapshot.mediaSources.firstOrNull()
    val mediaSourceId = selectedSource?.sourceId ?: snapshot.mediaSourceId
    val mediaPayload = remember(mediaSourceId) {
        JSONObject().apply {
            if (mediaSourceId.isNotBlank()) {
                put("sourceId", mediaSourceId)
            }
        }
    }
    val mediaTitle = (selectedSource?.displayTitle ?: snapshot.mediaTitle)
        .takeIf { it.isNotBlank() }
        ?.replace(" - VLC media player", "")
        ?.replace(" - MPC-HC", "")
        ?: "Медиа-контроллер"
    val mediaSubtitle = (selectedSource?.displaySubtitle ?: snapshot.mediaProcess)
        .takeIf { it.isNotBlank() }
        ?: "Системные media keys"
    val mediaIsPlaying = selectedSource?.isPlaying ?: snapshot.mediaIsPlaying
    val mediaDurationMs = (selectedSource?.durationMs ?: snapshot.mediaDurationMs).coerceAtLeast(0L)
    val mediaPositionMs = (selectedSource?.positionMs ?: snapshot.mediaPositionMs).coerceIn(0L, mediaDurationMs.takeIf { it > 0 } ?: Long.MAX_VALUE)
    val mediaArtwork = selectedSource?.artworkBase64 ?: snapshot.mediaArtworkBase64
    val artworkBitmap = remember(mediaArtwork) {
        decodeBase64Image(mediaArtwork)
    }

    LaunchedEffect(snapshot.systemVolume) {
        volume = snapshot.systemVolume.toFloat()
    }
    LaunchedEffect(snapshot.mediaSourceId, snapshot.mediaSources) {
        if (snapshot.mediaSources.none { it.sourceId == selectedSourceId }) {
            selectedSourceId = snapshot.mediaSourceId.ifBlank { snapshot.mediaSources.firstOrNull()?.sourceId.orEmpty() }
        } else if (selectedSourceId.isBlank()) {
            selectedSourceId = snapshot.mediaSourceId.ifBlank { snapshot.mediaSources.firstOrNull()?.sourceId.orEmpty() }
        }
    }
    LaunchedEffect(mediaPositionMs, mediaDurationMs, selectedSourceId, isSeeking) {
        if (!isSeeking) {
            seekValue = mediaPositionMs.toFloat()
        }
    }

    GlowCard {
        Text(mediaTitle, color = TextMain, style = androidx.compose.material3.MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis)
        Text(mediaSubtitle, color = TextMuted, style = androidx.compose.material3.MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(118.dp)
                .clip(RoundedCornerShape(18.dp))
                .background(Brush.linearGradient(listOf(Color(0xFF0E1B2B), Color(0xFF102A3A))))
                .border(1.dp, CardStroke, RoundedCornerShape(18.dp)),
            contentAlignment = Alignment.Center
        ) {
            if (artworkBitmap != null) {
                Image(
                    bitmap = artworkBitmap,
                    contentDescription = mediaTitle,
                    modifier = Modifier.fillMaxSize(),
                    contentScale = ContentScale.Crop
                )
                Box(
                    Modifier
                        .fillMaxSize()
                        .background(Brush.verticalGradient(listOf(Color.Transparent, Color(0xA0000000))))
                )
            } else {
                Icon(NexusIcon.Play, contentDescription = null, tint = Cyan, modifier = Modifier.size(46.dp))
            }
        }
        if (mediaDurationMs > 0) {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Slider(
                    value = seekValue.coerceIn(0f, mediaDurationMs.toFloat()),
                    onValueChange = {
                        isSeeking = true
                        seekValue = it
                    },
                    onValueChangeFinished = {
                        isSeeking = false
                        onCommand("media_seek_to", JSONObject(mediaPayload.toString()).put("positionMs", seekValue.toLong()))
                    },
                    valueRange = 0f..mediaDurationMs.toFloat(),
                    colors = sliderColors()
                )
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(formatMediaTime(if (isSeeking) seekValue.toLong() else mediaPositionMs), color = TextMuted)
                    Text(formatMediaTime(mediaDurationMs), color = TextMuted)
                }
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
            RoundControl(NexusIcon.Rewind, "-5 сек") { onCommand("media_seek_relative", JSONObject(mediaPayload.toString()).put("seconds", -5)) }
            PlayButton(isPlaying = mediaIsPlaying) { onCommand("media_play_pause", JSONObject(mediaPayload.toString())) }
            RoundControl(NexusIcon.Forward, "+5 сек") { onCommand("media_seek_relative", JSONObject(mediaPayload.toString()).put("seconds", 5)) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.CenterVertically) {
            SquareControl(NexusIcon.VolumeOff) { onCommand("volume_mute", JSONObject()) }
            Slider(
                value = volume,
                onValueChange = { volume = it },
                onValueChangeFinished = { onCommand("volume_set", JSONObject().put("value", volume.toInt())) },
                valueRange = 0f..100f,
                colors = sliderColors(),
                modifier = Modifier.weight(1f)
            )
            SquareControl(NexusIcon.Volume) { onCommand("volume_up", JSONObject().put("steps", 2)) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            ControlTile(NexusIcon.SkipPrevious, "Назад", Modifier.weight(1f)) { onCommand("media_prev", JSONObject(mediaPayload.toString())) }
            ControlTile(NexusIcon.SkipNext, "Вперёд", Modifier.weight(1f)) { onCommand("media_next", JSONObject(mediaPayload.toString())) }
            ControlTile(NexusIcon.Fullscreen, "Полный экран", Modifier.weight(1f)) { onCommand("media_fullscreen", JSONObject(mediaPayload.toString())) }
            ControlTile(NexusIcon.Subtitles, "Субтитры", Modifier.weight(1f)) { onCommand("media_subtitles", JSONObject(mediaPayload.toString())) }
        }
        if (snapshot.mediaSources.size > 1) {
            Text("Источники", color = TextMain, fontWeight = FontWeight.Bold)
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                snapshot.mediaSources.forEach { source ->
                    MediaSourceRow(
                        source = source,
                        selected = source.sourceId == mediaSourceId,
                        onClick = { selectedSourceId = source.sourceId }
                    )
                }
            }
        }
    }
}

@Composable
private fun ProgramsScreen(snapshot: PcSnapshot, onCommand: (String, JSONObject) -> Unit) {
    val userPrograms = snapshot.programs.mapIndexed { index, status ->
        status.toProgramItem(index) to status.running
    }
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        if (userPrograms.isEmpty()) {
            EmptyProgramsCard()
        }
        userPrograms.forEach { (item, running) ->
            ProgramRow(item = item, running = running) {
                onCommand("launcher_run", JSONObject().put("path", item.command))
            }
        }
    }
}

private fun ProgramStatus.toProgramItem(index: Int): ProgramItem {
    val cleanName = name.ifBlank {
        command.substringAfterLast('\\').substringAfterLast('/').substringBeforeLast('.', command)
    }
    val lower = cleanName.lowercase()
    val logo = when {
        "steam" in lower -> "steam"
        "discord" in lower -> "discord"
        "obs" in lower -> "obs"
        "chrome" in lower -> "chrome"
        "blender" in lower -> "blender"
        "code" in lower || "visual studio" in lower -> "vscode"
        "vlc" in lower -> "vlc"
        else -> "generic"
    }
    val glyph = cleanName.firstOrNull()?.uppercaseChar()?.toString() ?: "A"
    return ProgramItem(
        name = cleanName,
        command = command,
        color = ProgramColors[index % ProgramColors.size],
        glyph = glyph,
        logo = logo,
        iconBase64 = iconBase64
    )
}

@Composable
private fun EmptyProgramsCard() {
    GlowCard {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            Box(
                Modifier
                    .size(58.dp)
                    .clip(CircleShape)
                    .background(Color(0xFF14243A)),
                contentAlignment = Alignment.Center
            ) {
                Icon(NexusIcon.Grid, contentDescription = null, tint = Cyan, modifier = Modifier.size(30.dp))
            }
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(
                    "Список программ пуст",
                    color = TextMain,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Text(
                    "Добавьте программы в окне PC Server",
                    color = TextMuted,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            }
        }
    }
}

@Composable
private fun SystemScreen(snapshot: PcSnapshot, onCommand: (String, JSONObject) -> Unit) {
    var volume by remember { mutableFloatStateOf(snapshot.systemVolume.toFloat()) }
    var brightness by remember { mutableFloatStateOf((snapshot.screenBrightness ?: 45).toFloat()) }
    var confirmAction by remember { mutableStateOf<Pair<String, String>?>(null) }
    var showShutdownTimer by remember { mutableStateOf(false) }

    LaunchedEffect(snapshot.systemVolume) {
        volume = snapshot.systemVolume.toFloat()
    }
    LaunchedEffect(snapshot.screenBrightness) {
        snapshot.screenBrightness?.let { brightness = it.toFloat() }
    }

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SystemInfoCard(snapshot)
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            SystemActionTile(NexusIcon.Power, "Выключить", Modifier.weight(1f)) {
                confirmAction = "power_shutdown" to "Выключить ПК сейчас?"
            }
            SystemActionTile(NexusIcon.Refresh, "Перезагрузка", Modifier.weight(1f)) {
                confirmAction = "power_restart" to "Перезагрузить ПК сейчас?"
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            SystemActionTile(NexusIcon.Moon, "Сон", Modifier.weight(1f)) {
                confirmAction = "power_sleep" to "Перевести ПК в сон?"
            }
            SystemActionTile(NexusIcon.Lock, "Блокировка", Modifier.weight(1f)) {
                confirmAction = "power_lock" to "Заблокировать ПК?"
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            SystemActionTile(NexusIcon.Monitor, "Выключить экран", Modifier.weight(1f)) {
                confirmAction = "monitor_off" to "Выключить экран?"
            }
            SystemActionTile(NexusIcon.Power, "Таймер выключения", Modifier.weight(1f)) {
                showShutdownTimer = true
            }
        }
        SystemSliderCard(
            icon = NexusIcon.Volume,
            title = "Громкость системы",
            value = volume,
            onValueChange = { volume = it },
            onDone = { onCommand("volume_set", JSONObject().put("value", volume.toInt())) }
        )
        if (snapshot.screenBrightness != null) {
            SystemSliderCard(
                icon = NexusIcon.Sun,
                title = "Яркость экрана",
                value = brightness,
                onValueChange = { brightness = it },
                onDone = { onCommand("brightness_set", JSONObject().put("value", brightness.toInt())) }
            )
        }
        SystemSummaryCard(snapshot)
    }

    confirmAction?.let { (action, text) ->
        AlertDialog(
            onDismissRequest = { confirmAction = null },
            containerColor = CardBg,
            titleContentColor = TextMain,
            textContentColor = TextMuted,
            shape = RoundedCornerShape(24.dp),
            title = { Text("Подтвердите действие", fontWeight = FontWeight.Bold) },
            text = { Text(text) },
            confirmButton = {
                TextButton(onClick = {
                    onCommand(action, JSONObject())
                    confirmAction = null
                }) {
                    Text("Да", color = Cyan)
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmAction = null }) {
                    Text("Отмена", color = TextMuted)
                }
            }
        )
    }

    if (showShutdownTimer) {
        AlertDialog(
            onDismissRequest = { showShutdownTimer = false },
            containerColor = CardBg,
            titleContentColor = TextMain,
            textContentColor = TextMuted,
            shape = RoundedCornerShape(24.dp),
            title = { Text("Таймер выключения", fontWeight = FontWeight.Bold) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text("Выберите, через сколько выключить ПК.", color = TextMuted)
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
                        CapsuleButton("15 мин", filled = false, compact = true) {
                            onCommand("power_shutdown_timer", JSONObject().put("seconds", 15 * 60))
                            showShutdownTimer = false
                        }
                        CapsuleButton("30 мин", filled = true, compact = true) {
                            onCommand("power_shutdown_timer", JSONObject().put("seconds", 30 * 60))
                            showShutdownTimer = false
                        }
                        CapsuleButton("60 мин", filled = false, compact = true) {
                            onCommand("power_shutdown_timer", JSONObject().put("seconds", 60 * 60))
                            showShutdownTimer = false
                        }
                    }
                    TextButton(onClick = {
                        onCommand("power_shutdown_cancel", JSONObject())
                        showShutdownTimer = false
                    }) {
                        Text("Отменить таймер", color = Cyan)
                    }
                }
            },
            confirmButton = {},
            dismissButton = {
                TextButton(onClick = { showShutdownTimer = false }) {
                    Text("Закрыть", color = TextMuted)
                }
            }
        )
    }
}

@Composable
private fun SystemInfoCard(snapshot: PcSnapshot) {
    GlowCard {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            Box(
                Modifier
                    .size(92.dp)
                    .clip(CircleShape)
                    .background(Brush.radialGradient(listOf(Color(0x3328B8FF), Color(0x11162535)))),
                contentAlignment = Alignment.Center
            ) {
                Icon(NexusIcon.Computer, contentDescription = null, tint = Cyan, modifier = Modifier.size(46.dp))
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(7.dp)) {
                Text(snapshot.hostname.ifBlank { "Ждём данные" }, color = TextMain, style = androidx.compose.material3.MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(NexusIcon.Grid, contentDescription = null, tint = Cyan, modifier = Modifier.size(20.dp))
                    Text(snapshot.osName.ifBlank { "Windows" }, color = TextMuted, style = androidx.compose.material3.MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                Text("Время работы", color = TextMuted)
                Text(snapshot.uptime.ifBlank { "Ждём" }, color = Cyan, style = androidx.compose.material3.MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
            }
            Row(
                modifier = Modifier
                    .clip(RoundedCornerShape(12.dp))
                    .border(1.dp, CardStroke, RoundedCornerShape(12.dp))
                    .padding(horizontal = 12.dp, vertical = 9.dp),
                horizontalArrangement = Arrangement.spacedBy(7.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Box(Modifier.size(10.dp).clip(CircleShape).background(Green))
                Text("Онлайн", color = TextMain)
            }
        }
    }
}

@Composable
private fun RowScope.SystemActionTile(icon: NexusIcon, label: String, modifier: Modifier = Modifier, onClick: () -> Unit) {
    PressableBox(modifier.height(142.dp).clip(RoundedCornerShape(20.dp)), onClick = onClick) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(
                Modifier
                    .size(72.dp)
                    .clip(CircleShape)
                    .background(Color(0x1A28B8FF))
                    .border(2.dp, CardStroke, CircleShape),
                contentAlignment = Alignment.Center
            ) {
                Icon(icon, contentDescription = label, tint = Teal, modifier = Modifier.size(36.dp))
            }
            Text(label, color = TextMain, style = androidx.compose.material3.MaterialTheme.typography.titleMedium, textAlign = TextAlign.Center)
        }
    }
}

@Composable
private fun SystemSliderCard(
    icon: NexusIcon,
    title: String,
    value: Float,
    onValueChange: (Float) -> Unit,
    onDone: () -> Unit
) {
    GlowCard {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            Box(
                Modifier
                    .size(62.dp)
                    .clip(CircleShape)
                    .background(Color(0xFF162235))
                    .border(1.dp, CardStroke, CircleShape),
                contentAlignment = Alignment.Center
            ) {
                Icon(icon, contentDescription = title, tint = TextMain, modifier = Modifier.size(30.dp))
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(title, color = TextMain, style = androidx.compose.material3.MaterialTheme.typography.titleMedium)
                    Text("${value.toInt()}%", color = Cyan, style = androidx.compose.material3.MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                }
                Slider(
                    value = value,
                    onValueChange = onValueChange,
                    onValueChangeFinished = onDone,
                    valueRange = 0f..100f,
                    colors = sliderColors()
                )
            }
        }
    }
}

@Composable
private fun SystemSummaryCard(snapshot: PcSnapshot) {
    GlowCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            SummaryCell(NexusIcon.Wave, "Активные\nпроцессы", snapshot.processCount.takeIf { it > 0 }?.toString() ?: "Ждём", Cyan, Modifier.weight(1f))
            DividerLine()
            SummaryCell(NexusIcon.Battery, "Питание", snapshot.powerStatus.ifBlank { "Нормально" }, Green, Modifier.weight(1f))
            DividerLine()
            SummaryCell(NexusIcon.Refresh, "Последнее\nдействие", snapshot.lastAction.ifBlank { "Нет" }, Cyan, Modifier.weight(1f))
        }
    }
}

@Composable
private fun SummaryCell(icon: NexusIcon, title: String, value: String, color: Color, modifier: Modifier) {
    Row(modifier = modifier, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Box(
            Modifier
                .size(46.dp)
                .clip(CircleShape)
                .background(Color(0xFF162235))
                .border(1.dp, CardStroke, CircleShape),
            contentAlignment = Alignment.Center
        ) {
            Icon(icon, contentDescription = null, tint = TextMain, modifier = Modifier.size(24.dp))
        }
        Column {
            Text(title, color = TextMuted, style = androidx.compose.material3.MaterialTheme.typography.bodySmall)
            Text(value, color = color, style = androidx.compose.material3.MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun DividerLine() {
    Box(
        Modifier
            .width(1.dp)
            .height(58.dp)
            .background(CardStroke)
    )
}

@Composable
private fun MetricCard(icon: NexusIcon, title: String, load: Int?, color: Color, modifier: Modifier, history: List<Float>) {
    val loadText = load?.let { "$it%" } ?: "N/A"
    val progress = load?.let { it / 100f } ?: 0f

    GlowCard(modifier) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(icon, contentDescription = null, tint = TextMain, modifier = Modifier.size(26.dp))
                Text(
                    title,
                    color = TextMain,
                    fontWeight = FontWeight.Bold,
                    style = androidx.compose.material3.MaterialTheme.typography.titleLarge,
                    maxLines = 1,
                    overflow = TextOverflow.Clip
                )
            }
            Text("Загрузка", color = TextMuted, maxLines = 1)
        }
        Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            RingGauge(loadText, "", progress.coerceIn(0f, 1f), color, Modifier.size(118.dp))
        }
        Sparkline(values = history, color = color, modifier = Modifier.fillMaxWidth().height(58.dp))
    }
}

@Composable
private fun UsageCard(icon: NexusIcon, title: String, value: String, suffix: String, detail: String, modifier: Modifier, progress: Float) {
    GlowCard(modifier) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(icon, contentDescription = null, tint = TextMain, modifier = Modifier.size(26.dp))
            Text(title, color = TextMain, fontWeight = FontWeight.Bold, style = androidx.compose.material3.MaterialTheme.typography.titleLarge)
        }
        Text("Использование", color = TextMuted)
        Row(verticalAlignment = Alignment.Bottom) {
            Text(value, color = Cyan, style = androidx.compose.material3.MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold)
            Text(" $suffix", color = TextMuted, style = androidx.compose.material3.MaterialTheme.typography.titleMedium)
        }
        ProgressBar(progress.coerceIn(0f, 1f), Cyan)
        Text(detail, color = Teal, fontWeight = FontWeight.Bold)
    }
}

@Composable
private fun SparkCard(icon: NexusIcon, title: String, label: String, value: String, color: Color, modifier: Modifier, history: List<Float>) {
    GlowCard(modifier) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(icon, contentDescription = null, tint = TextMain, modifier = Modifier.size(26.dp))
            Text(title, color = TextMain, fontWeight = FontWeight.Bold, style = androidx.compose.material3.MaterialTheme.typography.titleLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Text(label, color = TextMuted)
        Text(value, color = color, style = androidx.compose.material3.MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold)
        Sparkline(values = history, color = color, modifier = Modifier.fillMaxWidth().height(64.dp))
    }
}

@Composable
private fun RingOnlyCard(icon: NexusIcon, title: String, value: String, suffix: String, modifier: Modifier) {
    GlowCard(modifier) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(icon, contentDescription = null, tint = TextMain, modifier = Modifier.size(26.dp))
            Text(title, color = TextMain, fontWeight = FontWeight.Bold, style = androidx.compose.material3.MaterialTheme.typography.titleLarge)
        }
        Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            RingGauge(value, suffix, 0.63f, Teal, Modifier.size(142.dp))
        }
    }
}

@Composable
private fun NetworkCard(snapshot: PcSnapshot, modifier: Modifier, history: List<Float>) {
    GlowCard(modifier) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(NexusIcon.Network, contentDescription = null, tint = TextMain, modifier = Modifier.size(26.dp))
            Text("Сеть", color = TextMain, fontWeight = FontWeight.Bold, style = androidx.compose.material3.MaterialTheme.typography.titleLarge)
        }
        Text("Текущая скорость", color = TextMuted)
        Text("${snapshot.networkTotalMbps} Mbps", color = Teal, style = androidx.compose.material3.MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold)
        Text("↓ ${snapshot.networkDownMbps} Mbps", color = Teal, fontWeight = FontWeight.Bold)
        Text("↑ ${snapshot.networkUpMbps} Mbps", color = Cyan, fontWeight = FontWeight.Bold)
        Sparkline(values = history, color = Teal, modifier = Modifier.fillMaxWidth().height(42.dp))
    }
}

@Composable
private fun ProgramRow(item: ProgramItem, running: Boolean, onClick: () -> Unit) {
    GlowCard {
        BoxWithConstraints {
            val compact = maxWidth < 390.dp
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(if (compact) 10.dp else 16.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                ProgramLogo(item = item, compact = compact)
                Column(
                    modifier = Modifier.weight(1f),
                    verticalArrangement = Arrangement.spacedBy(4.dp)
                ) {
                    Text(
                        item.name,
                        color = TextMain,
                        style = androidx.compose.material3.MaterialTheme.typography.titleLarge,
                        fontWeight = FontWeight.Bold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(10.dp).clip(CircleShape).background(if (running) Green else Color(0xFF64748B)))
                        Text(
                            if (running) "Запущено" else "Не запущено",
                            color = if (running) Green else TextMuted,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }
                }
                CapsuleButton(
                    if (running) "Открыть" else "Запустить",
                    filled = !running,
                    compact = compact,
                    onClick = onClick
                )
                if (!compact) {
                    SquareControl(if (running) NexusIcon.Close else NexusIcon.Play, onClick = onClick)
                }
            }
        }
    }
}

@Composable
private fun ProgramLogo(item: ProgramItem, compact: Boolean = false) {
    val bitmap = remember(item.iconBase64) {
        runCatching {
            if (item.iconBase64.isBlank()) return@runCatching null
            val bytes = Base64.decode(item.iconBase64, Base64.DEFAULT)
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
        }.getOrNull()
    }
    Box(
        Modifier
            .size(if (compact) 58.dp else 72.dp)
            .clip(CircleShape)
            .background(item.color),
        contentAlignment = Alignment.Center
    ) {
        if (bitmap != null) {
            Image(
                bitmap = bitmap,
                contentDescription = item.name,
                modifier = Modifier
                    .fillMaxSize()
                    .padding(if (compact) 10.dp else 12.dp)
            )
            return@Box
        }
        Canvas(Modifier.fillMaxSize()) {
            when (item.logo) {
                "steam" -> {
                    drawCircle(Color(0xFF0B2442), radius = size.minDimension * .49f)
                    drawCircle(Color.White, radius = size.minDimension * .19f, center = Offset(size.width * .62f, size.height * .34f), style = Stroke(width = 5.dp.toPx()))
                    drawLine(Color.White, Offset(size.width * .42f, size.height * .58f), Offset(size.width * .57f, size.height * .43f), strokeWidth = 5.dp.toPx(), cap = StrokeCap.Round)
                    drawCircle(Color.White, radius = size.minDimension * .13f, center = Offset(size.width * .34f, size.height * .66f))
                }
                "discord" -> {
                    drawRoundRect(Color(0xFFEEF2FF), topLeft = Offset(size.width * .24f, size.height * .31f), size = Size(size.width * .52f, size.height * .34f), cornerRadius = androidx.compose.ui.geometry.CornerRadius(18.dp.toPx()))
                    drawCircle(item.color, radius = 4.dp.toPx(), center = Offset(size.width * .42f, size.height * .48f))
                    drawCircle(item.color, radius = 4.dp.toPx(), center = Offset(size.width * .58f, size.height * .48f))
                    drawArc(Color(0xFFEEF2FF), 205f, 130f, false, topLeft = Offset(size.width * .28f, size.height * .52f), size = Size(size.width * .44f, size.height * .25f), style = Stroke(4.dp.toPx(), cap = StrokeCap.Round))
                }
                "obs" -> {
                    drawCircle(Color.White, radius = size.minDimension * .42f, style = Stroke(width = 4.dp.toPx()))
                    drawCircle(Color.White.copy(alpha = .92f), radius = size.minDimension * .18f, center = Offset(size.width * .5f, size.height * .32f))
                    drawCircle(Color.White.copy(alpha = .92f), radius = size.minDimension * .18f, center = Offset(size.width * .34f, size.height * .6f))
                    drawCircle(Color.White.copy(alpha = .92f), radius = size.minDimension * .18f, center = Offset(size.width * .66f, size.height * .6f))
                }
                "chrome" -> {
                    drawArc(Color(0xFFEF4444), -30f, 120f, true, size = Size(size.width, size.height))
                    drawArc(Color(0xFFFACC15), 90f, 120f, true, size = Size(size.width, size.height))
                    drawArc(Color(0xFF22C55E), 210f, 120f, true, size = Size(size.width, size.height))
                    drawCircle(Color(0xFF3B82F6), radius = size.minDimension * .22f)
                    drawCircle(Color.White, radius = size.minDimension * .24f, style = Stroke(width = 3.dp.toPx()))
                }
                "blender" -> {
                    drawCircle(Color.White, radius = size.minDimension * .19f, center = Offset(size.width * .58f, size.height * .54f), style = Stroke(width = 5.dp.toPx()))
                    drawLine(Color.White, Offset(size.width * .22f, size.height * .35f), Offset(size.width * .57f, size.height * .35f), strokeWidth = 6.dp.toPx(), cap = StrokeCap.Round)
                    drawLine(Color.White, Offset(size.width * .32f, size.height * .2f), Offset(size.width * .58f, size.height * .52f), strokeWidth = 6.dp.toPx(), cap = StrokeCap.Round)
                    drawLine(Color.White, Offset(size.width * .22f, size.height * .72f), Offset(size.width * .45f, size.height * .47f), strokeWidth = 6.dp.toPx(), cap = StrokeCap.Round)
                }
                "vscode" -> {
                    val path = Path().apply {
                        moveTo(size.width * .22f, size.height * .5f)
                        lineTo(size.width * .44f, size.height * .28f)
                        lineTo(size.width * .72f, size.height * .18f)
                        lineTo(size.width * .72f, size.height * .82f)
                        lineTo(size.width * .44f, size.height * .72f)
                        close()
                    }
                    drawPath(path, Color(0xFFDBEAFE))
                    drawLine(item.color, Offset(size.width * .44f, size.height * .28f), Offset(size.width * .44f, size.height * .72f), strokeWidth = 7.dp.toPx(), cap = StrokeCap.Round)
                }
                "vlc" -> {
                    val cone = Path().apply {
                        moveTo(size.width * .5f, size.height * .18f)
                        lineTo(size.width * .25f, size.height * .78f)
                        lineTo(size.width * .75f, size.height * .78f)
                        close()
                    }
                    drawPath(cone, Color.White)
                    drawPath(cone, Color(0xFFFF7A00), style = Stroke(width = 4.dp.toPx()))
                    drawLine(Color(0xFFFF7A00), Offset(size.width * .39f, size.height * .47f), Offset(size.width * .61f, size.height * .47f), strokeWidth = 5.dp.toPx())
                    drawLine(Color(0xFFFF7A00), Offset(size.width * .33f, size.height * .62f), Offset(size.width * .67f, size.height * .62f), strokeWidth = 5.dp.toPx())
                }
                else -> drawCircle(Color.White.copy(alpha = .15f), radius = size.minDimension * .42f)
            }
        }
        if (item.logo !in setOf("steam", "discord", "obs", "chrome", "blender", "vscode", "vlc")) {
            Text(item.glyph, color = Color.White, style = androidx.compose.material3.MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Black)
        }
    }
}

@Composable
private fun GlowCard(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Card(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        colors = CardDefaults.cardColors(containerColor = CardBg)
    ) {
        Column(
            modifier = Modifier
                .border(1.dp, CardStroke, RoundedCornerShape(20.dp))
                .background(Brush.radialGradient(listOf(Color(0x1C28B8FF), Color.Transparent), radius = 560f))
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
            content = content
        )
    }
}

@Composable
private fun Icon(
    icon: NexusIcon,
    contentDescription: String?,
    tint: Color,
    modifier: Modifier = Modifier
) {
    Image(
        painter = painterResource(id = icon.resId),
        contentDescription = contentDescription,
        colorFilter = ColorFilter.tint(tint),
        modifier = modifier
    )
}

@Composable
private fun RingGauge(value: String, suffix: String, progress: Float, color: Color, modifier: Modifier) {
    Box(modifier, contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val stroke = Stroke(width = 12.dp.toPx(), cap = StrokeCap.Round)
            drawArc(Color(0xFF222D42), -210f, 300f, false, style = stroke)
            drawArc(Brush.sweepGradient(listOf(Cyan, color, Cyan)), -210f, 300f * progress, false, style = stroke)
        }
        Row(verticalAlignment = Alignment.Bottom) {
            Text(value, color = TextMain, style = androidx.compose.material3.MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold)
            Text(suffix, color = TextMain, style = androidx.compose.material3.MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun Sparkline(values: List<Float>, color: Color, modifier: Modifier) {
    Canvas(modifier) {
        val points = normalizeHistory(values)
        val path = Path()
        val w = size.width
        val h = size.height
        points.forEachIndexed { index, point ->
            val x = w * index / (points.size - 1)
            val y = h - h * point
            if (index == 0) path.moveTo(x, y) else path.lineTo(x, y)
        }
        val fill = Path().apply {
            addPath(path)
            lineTo(w, h)
            lineTo(0f, h)
            close()
        }
        drawPath(fill, Brush.verticalGradient(listOf(color.copy(alpha = .35f), Color.Transparent)))
        drawPath(path, color, style = Stroke(width = 3.dp.toPx(), cap = StrokeCap.Round))
    }
}

@Composable
private fun ProgressBar(progress: Float, color: Color) {
    Box(Modifier.fillMaxWidth().height(14.dp).clip(RoundedCornerShape(5.dp)).background(Track)) {
        Box(Modifier.fillMaxWidth(progress).height(14.dp).clip(RoundedCornerShape(5.dp)).background(Brush.horizontalGradient(listOf(Cyan, Teal))))
    }
}

@Composable
private fun sliderColors() = SliderDefaults.colors(
    thumbColor = Teal,
    activeTrackColor = Cyan,
    inactiveTrackColor = Track
)

@Composable
private fun RoundControl(icon: NexusIcon, label: String, onClick: () -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        PressableBox(Modifier.size(88.dp).clip(CircleShape), onClick = onClick) {
            Icon(icon, contentDescription = label, tint = TextMain, modifier = Modifier.size(38.dp))
        }
        Text(label, color = TextMuted)
    }
}

@Composable
private fun PlayButton(isPlaying: Boolean, onClick: () -> Unit) {
    PressableBox(Modifier.size(126.dp).clip(CircleShape), onClick = onClick, prominent = true) {
        Icon(
            if (isPlaying) NexusIcon.Pause else NexusIcon.Play,
            contentDescription = if (isPlaying) "Пауза" else "Воспроизвести",
            tint = TextMain,
            modifier = Modifier.size(56.dp)
        )
    }
}

@Composable
private fun MediaSourceRow(source: MediaSourceUi, selected: Boolean, onClick: () -> Unit) {
    PressableBox(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp)),
        onClick = onClick,
        prominent = selected
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 14.dp, vertical = 12.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                Modifier
                    .size(10.dp)
                    .clip(CircleShape)
                    .background(if (source.isPlaying) Green else TextMuted.copy(alpha = 0.6f))
            )
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(source.displayTitle, color = TextMain, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(source.displaySubtitle, color = TextMuted, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Text(
                when (source.mediaKind) {
                    "video" -> "Видео"
                    "music" -> "Музыка"
                    else -> source.appName.ifBlank { "Источник" }
                },
                color = if (selected) Cyan else TextMuted,
                maxLines = 1
            )
        }
    }
}

@Composable
private fun SquareControl(icon: NexusIcon, onClick: () -> Unit) {
    PressableBox(Modifier.size(66.dp).clip(RoundedCornerShape(18.dp)), onClick = onClick) {
        Icon(icon, contentDescription = null, tint = TextMain, modifier = Modifier.size(30.dp))
    }
}

@Composable
private fun RowScope.ControlTile(icon: NexusIcon, label: String, modifier: Modifier = Modifier, onClick: () -> Unit) {
    PressableBox(modifier.height(92.dp).clip(RoundedCornerShape(18.dp)), onClick = onClick) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Icon(icon, contentDescription = label, tint = TextMain, modifier = Modifier.size(30.dp))
            Text(label, color = TextMuted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun PressableBox(modifier: Modifier, prominent: Boolean = false, onClick: () -> Unit, content: @Composable () -> Unit) {
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) .96f else 1f, spring(dampingRatio = .72f), label = "press")
    Box(
        modifier
            .scale(scale)
            .background(if (prominent) Color(0x2228B8FF) else Color(0xFF162235))
            .border(if (prominent) 3.dp else 1.dp, if (prominent) Cyan else CardStroke, if (prominent) CircleShape else RoundedCornerShape(18.dp))
            .clickable(interactionSource = source, indication = null, onClick = onClick),
        contentAlignment = Alignment.Center
    ) { content() }
}

@Composable
private fun CapsuleButton(text: String, filled: Boolean, compact: Boolean = false, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        shape = RoundedCornerShape(12.dp),
        modifier = Modifier
            .height(if (compact) 44.dp else 48.dp)
            .width(if (compact) 112.dp else 132.dp),
        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 0.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = if (filled) Cyan else Color.Transparent,
            contentColor = if (filled) TextMain else Cyan
        ),
        border = if (filled) null else androidx.compose.foundation.BorderStroke(1.dp, Cyan)
    ) {
        Text(text, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
private fun BottomNav(selected: MainTab, onSelect: (MainTab) -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(Color(0xEE0B1624))
            .padding(horizontal = 18.dp, vertical = 12.dp),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        MainTab.entries.forEach { item ->
            BottomNavItem(item, selected == item, onClick = { onSelect(item) })
        }
    }
}

@Composable
private fun RowScope.BottomNavItem(item: MainTab, active: Boolean, onClick: () -> Unit) {
    val scale by animateFloatAsState(if (active) 1f else .94f, tween(180), label = "nav")
    Column(
        modifier = Modifier
            .weight(1f)
            .scale(scale)
            .clip(RoundedCornerShape(34.dp))
            .background(if (active) Color(0xFF14243A) else Color.Transparent)
            .clickable(onClick = onClick)
            .padding(vertical = 12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(4.dp)
    ) {
        Icon(
            icon = item.icon,
            contentDescription = item.title,
            tint = if (active) Cyan else TextMuted,
            modifier = Modifier.size(28.dp)
        )
        Text(
            item.title,
            color = if (active) Cyan else TextMuted,
            fontWeight = if (active) FontWeight.Bold else FontWeight.Medium,
            style = androidx.compose.material3.MaterialTheme.typography.bodyMedium,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis
        )
    }
}

@Composable
private fun ConnectionDialog(
    connected: Boolean,
    host: String,
    port: String,
    token: String,
    status: String,
    versionText: String,
    updateStatus: String,
    onHostChange: (String) -> Unit,
    onPortChange: (String) -> Unit,
    onTokenChange: (String) -> Unit,
    onPing: () -> Unit,
    onDiscover: () -> Unit,
    onScanQr: () -> Unit,
    onOpenPrograms: () -> Unit,
    onRefreshPrograms: () -> Unit,
    onCheckUpdates: () -> Unit,
    onDismiss: () -> Unit
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {},
        containerColor = CardBg,
        titleContentColor = TextMain,
        textContentColor = TextMuted,
        shape = RoundedCornerShape(28.dp),
        title = { Text("Подключение к ПК", fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Text(versionText, color = TextMuted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("1. Откройте Nexus Remote PC на компьютере.", color = TextMuted, lineHeight = 19.sp)
                Text("2. Нажмите “Найти ПК” или сканируйте QR из окна PC-приложения.", color = TextMuted, lineHeight = 19.sp)
                Text("3. Подтвердите новое устройство на ПК.", color = TextMuted, lineHeight = 19.sp)
                SoftTextField(host, onHostChange, "IP компьютера", KeyboardType.Uri)
                SoftTextField(port, onPortChange, "Порт", KeyboardType.Number)
                if (token.isNotBlank()) {
                    Text("Устройство сопряжено", color = Green, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Button(
                        onClick = onDiscover,
                        modifier = Modifier.weight(1f).height(48.dp),
                        shape = RoundedCornerShape(14.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF162235))
                    ) { Text("Найти ПК", fontWeight = FontWeight.Bold, maxLines = 1) }
                    Button(
                        onClick = onOpenPrograms,
                        modifier = Modifier.weight(1f).height(48.dp),
                        shape = RoundedCornerShape(14.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF162235))
                    ) { Text("Программы", fontWeight = FontWeight.Bold, maxLines = 1) }
                }
                Button(
                    onClick = onScanQr,
                    modifier = Modifier.fillMaxWidth().height(48.dp),
                    shape = RoundedCornerShape(14.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF162235))
                ) {
                    Text("Сканировать QR сопряжения", fontWeight = FontWeight.Bold, maxLines = 1)
                }
                Button(
                    onClick = onPing,
                    modifier = Modifier.fillMaxWidth().height(54.dp),
                    shape = RoundedCornerShape(16.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Cyan)
                ) {
                    Text("Проверить связь", fontWeight = FontWeight.Bold)
                }
                TextButton(onClick = onRefreshPrograms, modifier = Modifier.align(Alignment.End)) {
                    Text("Обновить список программ", color = Cyan)
                }
                TextButton(onClick = onCheckUpdates, modifier = Modifier.align(Alignment.End)) {
                    Text("Проверить обновления", color = Cyan)
                }
                Text(updateStatus, color = TextMuted, maxLines = 2, overflow = TextOverflow.Ellipsis)
                Text(
                    status,
                    color = if (status.startsWith("Нет связи") || status.startsWith("Ошибка") || status.contains("недоступен", true)) Danger else TextMuted,
                    maxLines = 3,
                    overflow = TextOverflow.Ellipsis
                )
                if (connected) {
                    TextButton(onClick = onDismiss, modifier = Modifier.align(Alignment.End)) {
                        Text("Закрыть")
                    }
                }
            }
        }
    )
}

@Composable
private fun SoftTextField(value: String, onValueChange: (String) -> Unit, label: String, type: KeyboardType, modifier: Modifier = Modifier) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = type),
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        colors = OutlinedTextFieldDefaults.colors(
            focusedTextColor = TextMain,
            unfocusedTextColor = TextMain,
            focusedLabelColor = Cyan,
            unfocusedLabelColor = TextMuted,
            focusedBorderColor = Cyan,
            unfocusedBorderColor = CardStroke,
            cursorColor = Cyan,
            focusedContainerColor = Color(0x33162035),
            unfocusedContainerColor = Color(0x33162035)
        )
    )
}

private fun shortcut(onCommand: (String, JSONObject) -> Unit, keys: List<String>) {
    onCommand("keyboard_shortcut", JSONObject().put("keys", JSONArray(keys)))
}

private fun formatGb(value: Double): String {
    val rounded = kotlin.math.round(value * 10.0) / 10.0
    return if (rounded % 1.0 == 0.0) rounded.toInt().toString() else rounded.toString()
}

private fun formatMediaTime(valueMs: Long): String {
    val totalSeconds = (valueMs / 1000).coerceAtLeast(0)
    val hours = totalSeconds / 3600
    val minutes = (totalSeconds % 3600) / 60
    val seconds = totalSeconds % 60
    return if (hours > 0) {
        "%d:%02d:%02d".format(hours, minutes, seconds)
    } else {
        "%02d:%02d".format(minutes, seconds)
    }
}

private fun decodeBase64Image(base64: String) = runCatching {
    if (base64.isBlank()) return@runCatching null
    val bytes = Base64.decode(base64, Base64.DEFAULT)
    BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
}.getOrNull()

private fun fallbackProgress(value: Int, fallback: Float): Float {
    return if (value > 0) value / 100f else fallback
}

private fun seedHistory(value: Int): List<Float> {
    val safe = value.coerceAtLeast(0).toFloat()
    return List(18) { index ->
        (safe * (0.72f + (index % 5) * 0.055f)).coerceAtLeast(1f)
    }
}

private fun appendHistory(history: List<Float>, value: Int): List<Float> {
    val next = value.coerceAtLeast(0).toFloat()
    if (next > 0f && history.maxOrNull()?.let { it <= 1.1f } == true) {
        return seedHistory(value)
    }
    return (history + next).takeLast(24)
}

private fun normalizeHistory(values: List<Float>): List<Float> {
    if (values.isEmpty()) {
        return List(8) { 0.08f }
    }
    val max = values.maxOrNull()?.coerceAtLeast(1f) ?: 1f
    return values.map { value ->
        (0.08f + (value / max) * 0.84f).coerceIn(0.08f, 0.96f)
    }
}

@Preview(showBackground = true)
@Composable
fun RemoteAppPreview() {
    NexusRemoteTheme(dynamicColor = false) {
        RemoteApp(
            status = "Готово: ping",
            pcContext = PcContext(activeProcess = "firefox.exe", suggestedSection = "Browser", suggestedLabel = "Браузер"),
            pcSnapshot = PcSnapshot(
                cpuLoad = 47,
                ramPercent = 38,
                ramUsedGb = 12.3,
                ramTotalGb = 32.0,
                diskPercent = 62,
                diskUsedGb = 582,
                diskTotalGb = 931,
                networkTotalMbps = 124,
                networkDownMbps = 114,
                networkUpMbps = 10,
                programs = listOf(ProgramStatus("Steam", "steam.exe", true))
            ),
            onRefreshSnapshot = { _, _ -> },
            onRefreshContext = { _, _ -> }
        ) { _, _, _, _, _ -> }
    }
}
