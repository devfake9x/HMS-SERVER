import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
assert(root, 'usage: node http-importer-trust-source-evidence-fixture.mjs <HMS-IDF-root>');

const files = {
  authManager: path.join(root, 'components', 'hms-http-api', 'auth', 'auth_manager.c'),
  authHandler: path.join(root, 'components', 'hms-http-api', 'auth', 'auth_handler.c'),
  licenseApi: path.join(root, 'components', 'hms-http-api', 'license', 'license_api.c'),
  identity: path.join(root, 'components', 'util-device-identity', 'src', 'device_identity.c'),
};

const source = Object.fromEntries(
  Object.entries(files).map(([name, file]) => [name, fs.readFileSync(file, 'utf8')]),
);

// These assertions pin the existing human/license HTTP boundary. They do not
// treat it as proof of a device-bound importer bootstrap protocol.
assert(source.authManager.includes('auth_token_from_request'));
assert(source.authManager.includes('strncmp(header, "Bearer ", 7)'));
assert(source.authManager.includes('auth_validate_token(token, ctx_out)'));
assert(source.authManager.includes('auth_middleware_handler'));
assert(source.authHandler.includes('#define MAX_BODY_SIZE 2048'));
assert(source.authHandler.includes('req->content_len > MAX_BODY_SIZE'));
assert(source.authManager.includes('AUTH_MAX_LOGIN_ATTEMPTS 5'));
assert(source.authManager.includes('AUTH_LOCKOUT_SECONDS   300'));
assert(source.licenseApi.includes('#define LIC_BODY_MAX  2048'));
assert(source.licenseApi.includes('license_apply(s_buf, got, &mode, &reason)'));
assert(source.licenseApi.includes('u.handler = auth_middleware_handler'));
assert(source.licenseApi.includes('PERM_LICENSE_MANAGE'));
assert(source.licenseApi.includes('"/api/license/apply"'));
assert(source.identity.includes('device_identity_get_id'));
assert(!source.identity.includes('device_identity_set'));
assert(!source.identity.includes('nvs_set_str(h, DEVID_KEY_DEVICE_ID'));

const unsupportedPatterns = [
  'bootstrap_challenge',
  'import_nonce',
  'csrf',
  'CSRF',
  'device_bound_token',
];

function sha256(text) {
  return crypto.createHash('sha256').update(text).digest('hex');
}

const result = {
  evidence: 'CONTRACT_TESTED',
  scope: 'Selected HMS-IDF auth, license HTTP, and identity source files only',
  humanHttpAuth: {
    tokenScheme: 'Bearer Authorization header, with legacy X-Auth-Token fallback when Authorization is absent',
    authorization: 'auth_middleware_handler -> auth_require -> auth_validate_token -> permission check',
    loginBodyMaxBytes: 2048,
    loginRateLimit: { maxAttempts: 5, lockoutSeconds: 300 },
    source: [files.authManager, files.authHandler],
  },
  licenseHttp: {
    applyRoute: '/api/license/apply',
    requiredPermission: 'PERM_LICENSE_MANAGE',
    bodyMaxBytes: 2048,
    payload: 'raw LicenseEnvelope bytes',
    invokes: 'license_apply',
    source: files.licenseApi,
  },
  identity: {
    activeIdGetter: 'device_identity_get_id -> enroll/device_info/device_id',
    setterStatus: 'NOT_FOUND_IN_SELECTED_IDENTITY_COMPONENT',
    source: files.identity,
  },
  importerTrust: {
    status: 'NEW_FIRMWARE_CONTRACT',
    requiredButUnproven: [
      'device-bound bootstrap challenge/token',
      'one-time import nonce and replay protection',
      'local-only transport/authentication',
      'import-specific CSRF policy',
      'import-specific concurrency and recovery semantics',
      'candidate identity setter/validation seam',
    ],
    selectedScopeUnsupportedPatternSearch: unsupportedPatterns,
    limitation: 'Absence in these selected files is not a whole-repository proof; an importer fixture and implementation are still required.',
  },
  sourceDigests: Object.fromEntries(
    Object.entries(source).map(([name, text]) => [name, sha256(text)]),
  ),
};

console.log(JSON.stringify(result, null, 2));
