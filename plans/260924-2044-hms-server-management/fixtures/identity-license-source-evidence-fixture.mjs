import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2];
const outputPath = process.argv[3] || path.join(
  'plans',
  '260924-2044-hms-server-management',
  'fixtures',
  'identity-license-source-evidence.json',
);
assert(root, 'usage: node identity-license-source-evidence-fixture.mjs <HMS-IDF-root> [output]');

const files = {
  identityHeader: path.join(root, 'components', 'util-device-identity', 'include', 'device_identity.h'),
  identitySource: path.join(root, 'components', 'util-device-identity', 'src', 'device_identity.c'),
  licenseApi: path.join(root, 'components', 'hms-license', 'src', 'license_api.c'),
  licenseStorage: path.join(root, 'components', 'hms-license', 'src', 'license_storage.c'),
  authHeader: path.join(root, 'components', 'hms-auth', 'include', 'hms_auth.h'),
  licenseHttp: path.join(root, 'components', 'hms-http-api', 'license', 'license_api.c'),
};

for (const [name, file] of Object.entries(files)) {
  assert(fs.existsSync(file), `${name} source file is missing: ${file}`);
}

const read = (file) => fs.readFileSync(file, 'utf8');
const lineAt = (source, offset) => source.slice(0, offset).split(/\r?\n/).length;
const anchor = (source, needle) => {
  const offset = source.indexOf(needle);
  assert(offset >= 0, `missing source anchor: ${needle}`);
  return { needle, line: lineAt(source, offset) };
};
const digest = (source) => crypto.createHash('sha256').update(source).digest('hex');

const identityHeader = read(files.identityHeader);
const identitySource = read(files.identitySource);
const licenseApi = read(files.licenseApi);
const licenseStorage = read(files.licenseStorage);
const authHeader = read(files.authHeader);
const licenseHttp = read(files.licenseHttp);

const exportedIdentityFunctions = [...identityHeader.matchAll(/esp_err_t\s+(device_identity_[a-z0-9_]+)\s*\(/g)]
  .map((match) => match[1]);
assert.deepEqual(exportedIdentityFunctions, [
  'device_identity_get_mac',
  'device_identity_get_id',
  'device_identity_get_product_model',
  'device_identity_get_board_serial',
  'device_identity_format_mac',
]);
assert.equal(/device_identity_(?:set|write|put)[a-z0-9_]*/i.test(identityHeader), false);
assert.equal(/device_identity_(?:set|write|put)[a-z0-9_]*/i.test(identitySource), false);
assert.equal(/nvs_set_(?:str|blob)\s*\([^\n]*"device_id"/.test(identitySource), false);

const applyStart = licenseApi.indexOf('esp_err_t license_apply(');
const applyEnd = licenseApi.indexOf('\nesp_err_t license_remove(', applyStart);
assert(applyStart >= 0 && applyEnd > applyStart, 'license_apply body boundary missing');
const applyBody = licenseApi.slice(applyStart, applyEnd);
const exactPending = anchor(applyBody, 'memcmp(current->envelope, envelope, len) == 0');
const candidateCommit = anchor(applyBody, 'err = prv_commit_record_reconciled(current, &committed_seq);');
const finalize = anchor(applyBody, 'err = prv_finalize_clock_clear_pending(current, false,');
const publish = anchor(applyBody, 'err = prv_publish_record(current, out_mode, out_reason);');
assert(candidateCommit.line < finalize.line, 'license commit must precede clock-clear finalize in source');
assert(candidateCommit.line < publish.line, 'license commit must precede publication in source');
anchor(applyBody, 'current->clock_clear_pending = current->clock_clear_pending || !same_license;');
anchor(applyBody, 'license_replay_validate(dry, current,');

const markerChecks = [
  anchor(licenseStorage, '#define LIC_NVS_SLOT_A'),
  anchor(licenseStorage, '#define LIC_NVS_SLOT_B'),
  anchor(licenseStorage, '#define LIC_NVS_MARK_A'),
  anchor(licenseStorage, '#define LIC_NVS_MARK_B'),
  anchor(licenseStorage, 'marks[i] == disks[i].seq'),
  anchor(licenseStorage, 'disks[0].seq == disks[1].seq'),
  anchor(licenseStorage, 'return ESP_ERR_INVALID_CRC;'),
];
const authRoute = anchor(licenseHttp, 'license_apply_post');
const authPermission = anchor(licenseHttp, 'PERM_LICENSE_MANAGE');
const authRequire = anchor(authHeader, 'auth_require');

const output = {
  evidence: 'CONTRACT_TESTED',
  evidenceClass: 'SOURCE_PROVEN_IDENTITY_API_AND_LICENSE_JOURNAL_ORDER',
  sourceRoot: root,
  files: Object.fromEntries(Object.entries(files).map(([name, file]) => {
    const source = read(file);
    return [name, { path: file, sha256: digest(source) }];
  })),
  identity: {
    exportedFunctions: exportedIdentityFunctions,
    activeStorage: 'enroll/device_info/device_id',
    vendorStorage: 'vendor/device_info/product_model and board_serial',
    setterEvidence: 'NO_EXPORTED_OR_IMPLEMENTED_DEVICE_ID_SETTER_FOUND_IN_IDENTITY_COMPONENT',
    directLiteralDeviceIdWriteEvidence: 'NO_DIRECT_NVS_SET_LITERAL_DEVICE_ID_FOUND_IN_IDENTITY_SOURCE',
    anchors: [
      anchor(identityHeader, 'Lives in NVS partition'),
      anchor(identitySource, '#define DEVID_PART_ENROLL'),
      anchor(identitySource, 'device_identity_get_id'),
      anchor(identitySource, 'PR-1 documents this namespace; factory provisioning console cmd'),
    ],
  },
  licenseApply: {
    exactPendingByteMatch: exactPending,
    candidateCommit: candidateCommit,
    clockClearFinalize: finalize,
    publication: publish,
    replayValidation: anchor(applyBody, 'license_replay_validate(dry, current,'),
    anchors: markerChecks,
    httpAuthAnchors: [authRoute, authPermission, authRequire],
    interpretation: 'Durable journal ordering and exact-pending resume are source-proven; power-loss behavior at each write/marker and hardware recovery are not proven by this fixture.',
  },
  limitations: [
    'Static absence of a setter does not prove that no generated or out-of-tree provisioning code exists.',
    'Source ordering does not replace NVS power-loss, boot-recovery, or hardware tests.',
    'Human license permission middleware does not prove device-bound importer bootstrap or local-only transport.',
    'Candidate identity activation remains a new firmware contract.',
  ],
};

fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(output, null, 2)}\n`);
console.log(JSON.stringify({
  evidence: output.evidence,
  evidenceClass: output.evidenceClass,
  exportedIdentityFunctions,
  outputPath: path.resolve(outputPath),
}, null, 2));
