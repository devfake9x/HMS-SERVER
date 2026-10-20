const WebSocket = require('ws');
const http = require('http');

http.get('http://[::1]:9222/json', (res) => {
  let d = '';
  res.on('data', c => d += c);
  res.on('end', () => {
    const tabs = JSON.parse(d);
    const tab = tabs.find(t => t.type === 'page');
    console.log('Target tab:', tab.url);
    const ws = new WebSocket(tab.webSocketDebuggerUrl, {
      headers: { Host: 'localhost:9222' }
    });
    ws.on('open', () => {
      console.log('WS OPEN!');
      ws.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression: '1+1' } }));
    });
    ws.on('message', (data) => {
      console.log('RECEIVED MESSAGE:', data.toString());
      ws.close();
      process.exit(0);
    });
    ws.on('error', (err) => console.error('WS ERROR:', err));
  });
});
