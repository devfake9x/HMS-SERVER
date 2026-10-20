---
title: "CK brainstorm visible pass: HMS-SERVER pre-cook gate"
type: brainstorm-report
date: 2026-09-25
status: recommended
scope: HMS-SERVER management/control-plane readiness
skill: ck-brainstorm
---

# Problem-First Brainstorm

## 1. Solution-jumping diagnosis

The request to use a large autonomous goal risks treating prose as a substitute
for protocol evidence. The actual signal is that HMS-SERVER and HMS-IDF are at
different maturity levels and several security-sensitive contracts are only
partly proven. The failure mode is an implementation agent cooking endpoints,
MQTT topics, or an importer from assumptions that look plausible but are not
accepted by the device.

## 2. Underlying problem

HMS-SERVER needs a trustworthy handoff boundary between firmware evidence,
architecture decisions, implementation planning, and later production tests.
Every unsupported behavior must be visible, assigned to an owner phase, and
blocked at the correct boundary instead of being silently invented.

## 3. Assumption challenges

| Assumption | Risk if wrong | Validation |
|---|---|---|
| A longer goal prompt guarantees correct execution | Agents may skip source evidence and claim readiness from mocks | Require a readiness ledger with source paths, fixture output, owner, and evidence class |
| Firmware wildcard ingress implies a canonical server publish topic | ACL may permit the wrong leaf or fail to bind device identity | Add a firmware/EMQX integration fixture; until it passes return `CONTRACT_UNAVAILABLE` and publish zero messages |
| Human HTTP auth can protect device enrollment | A human session does not prove device binding, local-only access, or replay protection | Add a device-bound challenge/token, nonce, expiry, locality, and negative remote-call fixture |
| Model importer tests prove NVS recovery | Actual write and journal ordering can differ | Keep model tests separate and require power-loss tests after every partition and marker write |
| A current command catalog is compatible with every firmware build | Descriptor drift can change accepted actions or schemas | Pin catalog version/hash to a reproducible firmware manifest and deny unknown hashes |

## 4. Problem statement

- **Users/context:** firmware, backend, frontend, security, and operations contributors working across HMS-IDF and HMS-SERVER.
- **Struggle:** they need safe device provisioning and control for up to 1,000 devices, but device behavior is not fully represented by the server plan.
- **Cause:** protocol facts, new contracts, stale architecture documents, and implementation-dependent tests are mixed together.
- **Consequence:** cross-device commands, broken license recovery, unsafe bundle handling, false HA claims, and wasted implementation work.
- **Success:** the goal produces a consistent, evidence-backed plan with deterministic pre-cook checks and an explicit readiness state.

## 5. Alternative framings

### Frame A - Protocol compatibility gate

Focus on MQTT envelope/topic/QoS, command catalog, identity, license, and
importer evidence. This minimizes invented firmware behavior but can leave
backend, frontend, and operations contracts under-specified.

### Frame B - Plan consistency gate

Focus on reconciling `plan.md`, all eight phases, decision records, reports,
fixtures, and stale normative documents. This prevents contradictory guidance
but can produce polished documents without enough executable protocol proof.

### Frame C - Pre-cook evidence gate (recommended)

Combine Frames A and B. Permit plan, decision, fixture, and model-test work;
prohibit production implementation. Separate `DESIGN_CONTRACT_READY`,
`IMPLEMENTATION_VERIFIED`, and `RELEASE_READY` so the goal can finish without
pretending that unimplemented runtime behavior is proven.

## 6. Evidence status

Evidence is **strong** for the current firmware command envelope and MQTT
topic/QoS/retain/reconnect facts, including the 1,023-byte signed-header limit,
direct 32-byte HMAC, `key_ver` fencing, and the framed-body path.

Evidence is **incomplete and release-blocking** for:

- canonical server publish leaf and EMQX ACL/session identity propagation;
- device-bound local importer bootstrap and candidate identity activation;
- exact `license_apply()` pending/post-marker recovery;
- reproducible catalog manifests across firmware revisions;
- production KMS/signer/Data Protection integration;
- backend/frontend implementation tests and 1,000-device runtime load evidence.

These gaps are not implementation permission. They are explicit contract gates
owned by the relevant phases.

## 7. Validation plan

1. Re-scout HMS-IDF before every plan change and record source revision/digests.
2. Generate command, topic, framed-command, identity, license, importer, and
   catalog matrices with evidence classes.
3. Reconcile plan files and stale architecture documents before implementation.
4. Run deterministic fixtures and model tests without writing production source.
5. Keep negative tests for cross-device identity, replay, expiry, oversize,
   stale catalog/key generation, importer crash states, signer boundaries, and
   SignalR authorization epochs.
6. Assign implementation and release tests to their owning phases.
7. End with exactly one readiness state: `READY_TO_COOK` or
   `NOT_READY_TO_COOK`.

## 8. Stakeholder message

The CK brainstorm pass is complete and confirms that the project should remain
in pre-cook evidence work. The existing plan is not ready to cook because the
remaining gaps are device trust and recovery contracts, not missing prose. We
can continue autonomously by closing those evidence gates; once they are
proven or explicitly accepted as release limitations, the next handoff is
`ck-plan --tdd` output to `ck-cook --tdd`.

## Decision

Use Frame C. Keep the approved .NET/Vue control-plane scope and all 14 accepted
red-team findings. Do not invoke `ck-cook` in this goal. Do not mark the plan
ready while any P0/P1 gate remains unresolved or undocumented.

