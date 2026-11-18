// Konseptleri yerelde görmek için küçük statik sunucu: node src/serve.js
const http = require('http'), fs = require('fs'), path = require('path');
const root = path.resolve(__dirname, '..');
http.createServer((req, res) => {
  const name = path.basename(decodeURIComponent(req.url.split('?')[0])) || 'konsept-a.html';
  fs.readFile(path.join(root, name), (err, data) => {
    if (err) { res.writeHead(404); res.end('Bulunamadı'); return; }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    res.end(data);
  });
}).listen(8731, '127.0.0.1');
