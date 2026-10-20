const WebSocket = require('ws');
const http = require('http');

http.get('http://[::1]:9222/json', (res) => {
  let d = '';
  res.on('data', c => d += c);
  res.on('end', () => {
    const targets = JSON.parse(d);
    const tab = targets.find(t => t.type === 'page' && t.url.includes(':5173'));
    if (!tab) {
      console.log('No 5173 tab found:', targets.map(t => t.url));
      return;
    }
    console.log('Found tab:', tab.url);
    const ws = new WebSocket(tab.webSocketDebuggerUrl);
    ws.on('open', () => {
      console.log('WS opened successfully!');
      ws.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression: 'document.title' } }));
    });
    ws.on('message', (msg) => {
      console.log('Got CDP response:', msg.toString());
      ws.close();
    });
    ws.on('error', (err) => {
      console.error('WS error:', err);
    });
  });
});
