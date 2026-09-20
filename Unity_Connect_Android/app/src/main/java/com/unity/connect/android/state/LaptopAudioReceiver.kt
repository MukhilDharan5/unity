package com.unity.connect.android.state

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioTrack
import com.unity.connect.android.core.IncomingMessage
import java.io.DataInputStream
import java.net.ServerSocket
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlin.coroutines.coroutineContext
import kotlin.math.max

/** Receives one user-approved Windows loopback stream on an ephemeral local TCP port. */
class LaptopAudioReceiver(
    context: Context,
    private val scope: CoroutineScope,
    private val ready: suspend (String, Int) -> Boolean,
    private val changed: (Boolean, String?) -> Unit
) : AutoCloseable {
    private val audioManager = context.getSystemService(AudioManager::class.java)
    private val lock = Any()
    private var generation = 0L
    private var job: Job? = null
    private var server: ServerSocket? = null
    private var socket: Socket? = null
    private var track: AudioTrack? = null
    private var focus: AudioFocusRequest? = null
    var currentStreamId: String? = null
        private set

    fun start(request: IncomingMessage.AudioStreamStart) {
        val run: Long
        synchronized(lock) {
            generation++
            closeResourcesLocked()
            currentStreamId = request.streamId
            run = generation
        }
        changed(true, "Waiting for laptop audio…")
        val launched = scope.launch(Dispatchers.IO) { receive(request, run) }
        synchronized(lock) {
            if (generation == run) job = launched else launched.cancel()
        }
    }

    fun stop(streamId: String? = null) {
        val stopped = synchronized(lock) {
            if (currentStreamId == null || streamId != null && streamId != currentStreamId) return@synchronized false
            generation++
            closeResourcesLocked()
            currentStreamId = null
            true
        }
        if (stopped) changed(false, null)
    }

    private suspend fun receive(request: IncomingMessage.AudioStreamStart, run: Long) {
        var finalNotice: String? = null
        try {
            val listener = ServerSocket(0).apply { reuseAddress = true; soTimeout = 10_000 }
            if (!register(run) { server = listener }) { listener.close(); return }
            if (!ready(request.streamId, listener.localPort)) {
                finalNotice = "Laptop audio request could not be confirmed."
                return
            }

            val accepted = listener.accept().apply { tcpNoDelay = true }
            if (!register(run) { socket = accepted }) { accepted.close(); return }
            listener.close()
            synchronized(lock) { if (generation == run) server = null }

            val input = DataInputStream(accepted.getInputStream())
            val suppliedToken = ByteArray(16)
            input.readFully(suppliedToken)
            if (!MessageDigest.isEqual(request.token, suppliedToken)) {
                suppliedToken.fill(0)
                finalNotice = "Laptop audio connection was rejected."
                return
            }
            suppliedToken.fill(0)

            val output = createTrack(request.sampleRate, request.channels)
            if (!register(run) { track = output }) { output.release(); return }
            val focusRequest = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                .setAudioAttributes(AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                    .build())
                .setOnAudioFocusChangeListener { change ->
                    if (change == AudioManager.AUDIOFOCUS_LOSS) stop(request.streamId)
                }
                .build()
            if (audioManager.requestAudioFocus(focusRequest) == AudioManager.AUDIOFOCUS_REQUEST_FAILED) {
                finalNotice = "Phone audio output is busy."
                return
            }
            val ownsFocus = synchronized(lock) {
                if (generation == run) { focus = focusRequest; true } else false
            }
            if (!ownsFocus) {
                audioManager.abandonAudioFocusRequest(focusRequest)
                return
            }
            output.play()
            changed(true, "Playing laptop audio")

            val streamBytes = request.streamId.lowercase().toByteArray(Charsets.UTF_8)
            var expected = 0L
            while (true) {
                coroutineContext.ensureActive()
                val length = input.readInt()
                require(length in 26..16_384) { "Invalid audio record" }
                val record = ByteArray(length)
                input.readFully(record)
                val sequenceBytes = record.copyOfRange(0, 8)
                val sequence = ByteBuffer.wrap(sequenceBytes).order(ByteOrder.BIG_ENDIAN).long
                require(sequence == expected) { "Unexpected audio sequence" }
                val nonce = ByteArray(12)
                sequenceBytes.copyInto(nonce, 4)
                val aad = streamBytes + sequenceBytes
                val cipher = Cipher.getInstance("AES/GCM/NoPadding")
                cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(request.key, "AES"), GCMParameterSpec(128, nonce))
                cipher.updateAAD(aad)
                val pcm = cipher.doFinal(record, 8, record.size - 8)
                require(pcm.isNotEmpty() && pcm.size % (request.channels * 2) == 0) { "Invalid PCM payload" }
                var offset = 0
                while (offset < pcm.size) {
                    val written = output.write(pcm, offset, pcm.size - offset, AudioTrack.WRITE_BLOCKING)
                    require(written > 0) { "Phone audio output stopped" }
                    offset += written
                }
                expected++
            }
        } catch (_: CancellationException) {
            throw CancellationException()
        } catch (_: Exception) {
            finalNotice = finalNotice ?: "Laptop audio ended."
        } finally {
            request.key.fill(0)
            request.token.fill(0)
            finish(run, finalNotice)
        }
    }

    private fun createTrack(sampleRate: Int, channels: Int): AudioTrack {
        val channelMask = if (channels == 1) AudioFormat.CHANNEL_OUT_MONO else AudioFormat.CHANNEL_OUT_STEREO
        val minimum = AudioTrack.getMinBufferSize(sampleRate, channelMask, AudioFormat.ENCODING_PCM_16BIT)
        require(minimum > 0) { "Unsupported phone audio format" }
        val buffer = max(minimum, sampleRate * channels * 2 / 5)
        return AudioTrack.Builder()
            .setAudioAttributes(AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                .build())
            .setAudioFormat(AudioFormat.Builder()
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setSampleRate(sampleRate)
                .setChannelMask(channelMask)
                .build())
            .setTransferMode(AudioTrack.MODE_STREAM)
            .setBufferSizeInBytes(buffer)
            .build().also { require(it.state == AudioTrack.STATE_INITIALIZED) { "Phone audio output unavailable" } }
    }

    private inline fun register(run: Long, assign: () -> Unit): Boolean = synchronized(lock) {
        if (generation != run) false else { assign(); true }
    }

    private fun finish(run: Long, notice: String?) {
        val current = synchronized(lock) {
            if (generation != run) return@synchronized false
            generation++
            closeResourcesLocked()
            currentStreamId = null
            true
        }
        if (current) changed(false, notice)
    }

    private fun closeResourcesLocked() {
        job?.cancel(); job = null
        runCatching { socket?.close() }; socket = null
        runCatching { server?.close() }; server = null
        track?.let { audio -> runCatching { audio.pause() }; runCatching { audio.flush() }; runCatching { audio.release() } }
        track = null
        focus?.let { runCatching { audioManager.abandonAudioFocusRequest(it) } }; focus = null
    }

    override fun close() = stop()
}
