package com.unity.connect.android.core

import java.io.Closeable
import java.nio.ByteBuffer
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.Signature
import java.security.interfaces.ECPublicKey
import java.security.spec.ECGenParameterSpec
import java.security.spec.X509EncodedKeySpec
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.*

interface FramePipe : Closeable {
    suspend fun read(): ByteArray
    suspend fun write(frame: ByteArray)
}

data class SecurePeer(val publicKey: String, val name: String, val code: String)

/** Exact binding shared with Windows, documented in docs/SECURE-SESSION-v1.md. No Android APIs. */
class SecureSession private constructor(
    private val wire: FramePipe,
    private val tx: ByteArray,
    private val rx: ByteArray,
    private val transcript: ByteArray,
    val peer: SecurePeer
) : Closeable {
    private var txSequence = 0L
    private var rxSequence = 0L
    private val writes = Mutex()
    private var closed = false

    suspend fun send(frame: ByteArray) = writes.withLock {
        check(!closed && frame.size in 1..16384)
        try {
            check(txSequence < Long.MAX_VALUE)
            val sequence = ByteBuffer.allocate(8).putLong(++txSequence).array()
            val nonce = ByteArray(4) + sequence
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(tx, "AES"), GCMParameterSpec(128, nonce))
            cipher.updateAAD(transcript + sequence)
            wire.write(sequence + cipher.doFinal(frame))
        } catch (e: Exception) { close(); throw e }
    }

    suspend fun receive(): ByteArray {
        val record = wire.read()
        require(record.size in 25..MAX_WIRE_BYTES)
        val sequence = record.copyOfRange(0, 8)
        require(rxSequence < Long.MAX_VALUE && ByteBuffer.wrap(sequence).long == rxSequence + 1) { "Replayed record" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(rx, "AES"), GCMParameterSpec(128, ByteArray(4) + sequence))
        cipher.updateAAD(transcript + sequence)
        val plain = cipher.doFinal(record.copyOfRange(8, record.size))
        rxSequence++
        return plain
    }

    override fun close() { closed = true; wire.close() }

    companion object {
        const val MAX_WIRE_BYTES = 16408
        private val json = Json { ignoreUnknownKeys = false }
        private val b64 = Base64.getEncoder()
        private fun bytes(value: String) = Base64.getDecoder().decode(value)
        private fun hash(value: ByteArray) = MessageDigest.getInstance("SHA-256").digest(value)
        private fun packet(vararg fields: Pair<String, JsonPrimitive>) = buildJsonObject {
            put("version", 1)
            fields.forEach { (key, value) -> put(key, value) }
        }.toString().toByteArray(Charsets.UTF_8)
        fun control(type: String) = packet("type" to JsonPrimitive(type))
        fun parse(frame: ByteArray): JsonObject {
            require(frame.size in 1..MAX_WIRE_BYTES)
            return json.parseToJsonElement(frame.toString(Charsets.UTF_8)).jsonObject
        }
        private fun expect(value: JsonObject, type: String) {
            require(value["version"]?.jsonPrimitive?.int == 1 && value["type"]?.jsonPrimitive?.content == type)
        }
        private fun text(value: JsonObject, key: String) = value.getValue(key).jsonPrimitive.content
        fun ephemeralKey(): KeyPair = KeyPairGenerator.getInstance("EC").apply {
            initialize(ECGenParameterSpec("secp256r1"))
        }.generateKeyPair()
        private fun publicKey(encoded: String) = KeyFactory.getInstance("EC")
            .generatePublic(X509EncodedKeySpec(bytes(encoded))).also {
                require(it is ECPublicKey)
                val expected = (ephemeralKey().public as ECPublicKey).params
                require(it.params.curve == expected.curve && it.params.generator == expected.generator &&
                    it.params.order == expected.order && it.params.cofactor == expected.cofactor)
            }
        fun hkdf(secret: ByteArray, salt: ByteArray, info: String, size: Int): ByteArray {
            fun hmac(key: ByteArray, input: ByteArray): ByteArray = Mac.getInstance("HmacSHA256").run {
                init(SecretKeySpec(key, "HmacSHA256")); doFinal(input)
            }
            // All v1 outputs are at most one SHA-256 block.
            require(size in 1..32)
            val prk = hmac(salt, secret)
            return hmac(prk, info.toByteArray(Charsets.UTF_8) + byteArrayOf(1)).copyOf(size).also { prk.fill(0) }
        }
        suspend fun connect(wire: FramePipe, identity: KeyPair, name: String, client: Boolean = false,
            approve: suspend (SecurePeer) -> Boolean): SecureSession {
            var session: SecureSession? = null
            try {
                val ephemeral = ephemeralKey()
                val reveal = packet(
                    "type" to JsonPrimitive("key"), "role" to JsonPrimitive(if (client) "windows" else "android"),
                    "identity" to JsonPrimitive(b64.encodeToString(identity.public.encoded)),
                    "ephemeral" to JsonPrimitive(b64.encodeToString(ephemeral.public.encoded)),
                    "nonce" to JsonPrimitive(b64.encodeToString(ByteArray(32).also { SecureRandom().nextBytes(it) })),
                    "name" to JsonPrimitive(name.take(64)))
                wire.write(packet("type" to JsonPrimitive("commit"), "hash" to JsonPrimitive(b64.encodeToString(hash(reveal)))))
                val commitment = parse(wire.read()); expect(commitment, "commit")
                wire.write(reveal)
                val remoteReveal = wire.read()
                require(remoteReveal.size <= 2048 && MessageDigest.isEqual(hash(remoteReveal), bytes(text(commitment, "hash"))))
                val remote = parse(remoteReveal); expect(remote, "key")
                require(text(remote, "role") == if (client) "android" else "windows")
                require(bytes(text(remote, "nonce")).size == 32)
                val first = if (client) reveal else remoteReveal
                val second = if (client) remoteReveal else reveal
                val transcript = hash("UnityConnect/session/1\n".toByteArray(Charsets.UTF_8) +
                    ByteBuffer.allocate(4).putInt(first.size).array() + first +
                    ByteBuffer.allocate(4).putInt(second.size).array() + second)
                val signature = Signature.getInstance("SHA256withECDSA").run {
                    initSign(identity.private); update(transcript); sign()
                }
                wire.write(packet("type" to JsonPrimitive("proof"), "signature" to JsonPrimitive(b64.encodeToString(signature))))
                val proof = parse(wire.read()); expect(proof, "proof")
                val peerIdentity = text(remote, "identity")
                require(Signature.getInstance("SHA256withECDSA").run {
                    initVerify(publicKey(peerIdentity)); update(transcript); verify(bytes(text(proof, "signature")))
                }) { "Invalid identity proof" }
                val secret = KeyAgreement.getInstance("ECDH").run {
                    init(ephemeral.private); doPhase(publicKey(text(remote, "ephemeral")), true); generateSecret()
                }
                val sas = (ByteBuffer.wrap(hkdf(secret, transcript, "sas", 4)).int.toLong() and 0xffffffffL) % 1000000
                val peer = SecurePeer(peerIdentity, text(remote, "name").take(64), sas.toString().padStart(6, '0'))
                session = SecureSession(wire,
                    hkdf(secret, transcript, if (client) "windows-to-android" else "android-to-windows", 32),
                    hkdf(secret, transcript, if (client) "android-to-windows" else "windows-to-android", 32), transcript, peer)
                secret.fill(0)
                val accepted = approve(peer)
                session.send(packet("type" to JsonPrimitive("consent"), "accepted" to JsonPrimitive(accepted)))
                check(accepted) { "Pairing declined" }
                val consent = parse(session.receive()); expect(consent, "consent")
                check(consent.getValue("accepted").jsonPrimitive.boolean) { "Pairing declined by laptop" }
                session.send(control("hello"))
                expect(parse(session.receive()), "hello")
                return session
            } catch (e: Exception) { session?.close() ?: wire.close(); throw e }
        }
    }
}
