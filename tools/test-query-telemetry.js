const http = require('http');

http.get('http://127.0.0.1:5099/api/v1/auth/csrf', (res) => {
  let cookie = res.headers['set-cookie']?.[0]?.split(';')[0] || '';
  let d = '';
  res.on('data', c => d += c);
  res.on('end', () => {
    const token = JSON.parse(d).requestToken;
    const req = http.request('http://127.0.0.1:5099/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token, 'Cookie': cookie }
    }, (loginRes) => {
      let authCookie = loginRes.headers['set-cookie'].map(c => c.split(';')[0]).join('; ');
      http.get('http://127.0.0.1:5099/api/v1/devices/12133938-d604-4386-a58c-b454a5ef7ae7/telemetry', {
        headers: { 'Cookie': authCookie }
      }, (telRes) => {
        let telData = '';
        telRes.on('data', c => telData += c);
        telRes.on('end', () => {
          console.log('TELEMETRY STATUS:', telRes.statusCode);
          console.log(JSON.stringify(JSON.parse(telData), null, 2));
        });
      });
    });
    req.write(JSON.stringify({ email: 'admin@hms.local', password: 'AdminPassword123!' }));
    req.end();
  });
});
