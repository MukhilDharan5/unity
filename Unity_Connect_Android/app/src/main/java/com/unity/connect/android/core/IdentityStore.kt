package com.unity.connect.android.core

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.spec.ECGenParameterSpec

object IdentityStore {
    fun load(): KeyPair {
        val alias = "UnityConnect.Identity.P256.v1"
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        if (store.containsAlias(alias)) return KeyPair(store.getCertificate(alias).publicKey, store.getKey(alias, null) as PrivateKey)
        return KeyPairGenerator.getInstance("EC", "AndroidKeyStore").run {
            initialize(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY)
                .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256).build())
            generateKeyPair()
        }
    }
}
