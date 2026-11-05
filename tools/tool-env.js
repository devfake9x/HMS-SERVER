// tools/tool-env.js
// Environment-driven settings for the local dev/HIL tool scripts, so no account
// secret or machine-specific path lives in the repository. Secret values are never logged.
//
//   HMS_ADMIN_EMAIL / HMS_ADMIN_PASSWORD   HMS server account (dev seed or your own)
//   RTU_WEB_USERNAME / RTU_WEB_PASSWORD    RTU device web console account
//   HMS_TOOL_SCREENSHOT_DIR                where screenshots go (default: tools/screenshots)
const fs = require('fs');
const path = require('path');

function requireEnv(name) {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Environment variable ${name} is not set. Set it before running this tool.`);
  }
  return value;
}

function hmsAdminCredentials() {
  return {
    email: requireEnv('HMS_ADMIN_EMAIL'),
    password: requireEnv('HMS_ADMIN_PASSWORD'),
  };
}

function rtuWebCredentials() {
  return {
    username: requireEnv('RTU_WEB_USERNAME'),
    password: requireEnv('RTU_WEB_PASSWORD'),
  };
}

function screenshotDir() {
  const dir = process.env.HMS_TOOL_SCREENSHOT_DIR || path.join(__dirname, 'screenshots');
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

function screenshotFile(fileName) {
  return path.join(screenshotDir(), fileName);
}

module.exports = { hmsAdminCredentials, rtuWebCredentials, screenshotDir, screenshotFile };
