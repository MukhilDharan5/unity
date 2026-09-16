package com.unity.connect.android

import com.unity.connect.android.core.SecureSession
import com.unity.connect.android.lan.SocketFramePipe
import java.io.File
import java.net.ServerSocket
import java.security.KeyPairGenerator
import java.security.spec.ECGenParameterSpec
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Test

/**
 * Opt-in integration test used by the repository audit. A real .NET client connects over TCP,
 * completes the v1 handshake, compares the SAS, then exchanges encrypted application records.
 */
class CrossRuntimeInteropTest {
    @Test fun dotNetClientCompletesSecureSession() = runBlocking {
        val readyPath = System.getenv("UNITY_CONNECT_INTEROP_READY")
        assumeTrue("Set UNITY_CONNECT_INTEROP_READY to run the cross-runtime test.",
            !readyPath.isNullOrBlank())
        val ready = File(requireNotNull(readyPath))
        val identity = KeyPairGenerator.getInstance("EC").run {
            initialize(ECGenParameterSpec("secp256r1")); generateKeyPair()
        }
        ServerSocket(0).use { server ->
            server.soTimeout = 60_000
            ready.writeText(server.localPort.toString())
            val wire = SocketFramePipe(server.accept())
            val session = SecureSession.connect(wire, identity, "Android interop test") { true }
            try {
                val probe = Json.parseToJsonElement(session.receive().toString(Charsets.UTF_8)).jsonObject
                assertEquals("interop_probe", probe.getValue("type").jsonPrimitive.content)
                assertEquals(session.peer.code, probe.getValue("code").jsonPrimitive.content)
                val reply = buildJsonObject {
                    put("version", 1); put("type", "interop_reply"); put("code", session.peer.code)
                }.toString().toByteArray(Charsets.UTF_8)
                session.send(reply)
            } finally {
                session.close()
                ready.delete()
            }
        }
    }
}
