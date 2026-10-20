---
name: jwt-signing-key-hardening-conventions
description: auth JWT signing-key hardening (commit 94f246f) — sub↔sid bind, kill-switch/rotate, encrypt-at-rest; verified invariants + the reaper-vs-self-step-up-TTL bug
metadata:
  type: project
---

Commit `94f246f` "harden JWT signing key" landed 3 fixes + a signing-keys admin slice. Verified invariants (so future reviews don't re-derive):

**Fix 1 — sub↔sid bind (session-status-cache.ts):**
- cache value = session owner `account_id` (UUID) for live, `'0'` tombstone for dead, legacy `'1'` = treat-as-miss.
- UUID can never collide with `'0'`/`'1'` → no false match. `check(sid, expectedSub)` returns `owner === expectedSub`.
- chokepoint is `requireAuth` (auth.ts) + `requireMfaEnroll`. ALL step-up middlewares (require-step-up, require-self-step-up) run AFTER requireAuth and trust `c.var.user.id` → forge-escalation closed at the one chokepoint. Don't re-flag step-up tokens for missing sub↔sid bind.
- revoke() uses unconditional SET '0' (always beats legacy '1'); miss-fill uses SET NX (can't clobber tombstone). Race-safe, verified.

**Fix 2/3 — key-service.ts:**
- INVARIANT: only `getActiveSigningKey()` decrypts privateKeyPem (funnel via resolvePrivateKeyPem). Verified: all 5 `importPKCS8(key.privateKeyPem)` in jwt.ts are fed by getActiveSigningKey; getVerifyKey/listKeys only expose public/metadata. If a new caller reads `.privateKeyPem` from anywhere else it gets ciphertext — grep `.privateKeyPem` on any future change.
- encrypt-at-rest mirrors lib/totp.ts (AES-256-GCM, sha256 KDF of env key). prod-guard at env.ts:94 fail-closes on default JWT_KEY_ENCRYPTION_KEY. isPlaintextPem = startsWith('-----') (EncryptedPem JSON starts '{', no collision). lazy-migrate write is idempotent/race-safe.
- **KNOWN BUG (High, not yet fixed): reapRetiredKeys cutoff = now-(JWT_ACCESS_TTL+buffer)** but the active key ALSO signs self-step-up tokens with TTL=JWT_REFRESH_TTL_SECONDS (7d) via signSelfStepUpToken (jwt.ts:141). Reaper revokes a retiring key ~6min after rotate → valid 7d self-step-up tokens (passkey add / recovery-code regen) fail getVerifyKey → spurious `step_up_required` 401. Fix: cutoff must use max signed-token TTL (refresh TTL), or sign self-step-up with a longer-lived/separate key, or skip reap while any long-TTL token alive. Fails-closed (no security hole) but breaks UX. See [[auth-service-review-conventions]].
- emergencyRotate = rotate() THEN revokeKey(): tiny verify-window where compromised key still `retiring`+verifiable between the 2 awaits. Documented/accepted as minor.

**Slice signing-keys route:** never returns key material (only keyService.listKeys() whitelist + {kid}). GET=admin:jwks:read, rotate/revoke=admin:jwks:rotate+requireStepUp, reap=no step-up. requirePermission deny-by-default, roles resolved LIVE per-request. admin.super has wildcard `*:*`. exists-check before emergencyRotate avoids rotate-then-404 (but passes for already-`revoked` kid → unnecessary collateral rotation; minor).
