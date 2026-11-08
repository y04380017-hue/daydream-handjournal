import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import zlib from 'node:zlib';
import { fileURLToPath } from 'node:url';

const MAX_BODY = 900_000;
const MAX_STORE = 64 * 1024 * 1024;
const crc32 = buffer => { let crc = 0xffffffff; for (const byte of buffer) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); } return (crc ^ 0xffffffff) >>> 0; };
const digest = value => crypto.createHash('sha256').update(value).digest('hex');
const fail = (status, message) => { const error = new Error(message); error.status = status; throw error; };
const clean = (value, max, min = 1) => {
  if (typeof value !== 'string') fail(400, '文字字段缺失');
  value = value.trim();
  if (value.length < min || value.length > max || /[\u0000-\u0008\u000b\u000c\u000e-\u001f]/u.test(value)) fail(400, '文字长度或字符无效');
  return value;
};
function png(value, optional = false) {
  if (!value && optional) return '';
  if (typeof value !== 'string' || value.length > 800_000 || !/^[A-Za-z0-9+/]+={0,2}$/u.test(value)) fail(400, '只接受有限大小的PNG数据');
  const bytes = Buffer.from(value, 'base64');
  if (bytes.length < 33 || bytes.length > 600_000 || bytes.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a' || bytes.subarray(12, 16).toString() !== 'IHDR') fail(400, 'PNG头无效');
  const width = bytes.readUInt32BE(16), height = bytes.readUInt32BE(20);
  if (width < 1 || height < 1 || width > 1024 || height > 1024) fail(400, 'PNG尺寸上限1024×1024');
  let offset = 8, ended = false, header = false; const compressed = [];
  while (offset + 12 <= bytes.length) {
    const size = bytes.readUInt32BE(offset), end = offset + 12 + size;
    if (size > 600_000 || end > bytes.length) fail(400, 'PNG数据块不完整');
    const type = bytes.subarray(offset + 4, offset + 8).toString('ascii');
    if (crc32(bytes.subarray(offset + 4, offset + 8 + size)) !== bytes.readUInt32BE(offset + 8 + size)) fail(400, 'PNG校验失败');
    if (!header && type !== 'IHDR') fail(400, 'PNG缺少图像头');
    if (type === 'IHDR') { if (header || size !== 13 || bytes[offset + 16] !== 8 || ![0,2,4,6].includes(bytes[offset + 17]) || bytes[offset + 18] !== 0 || bytes[offset + 19] !== 0 || bytes[offset + 20] !== 0) fail(400, '仅接受8位非交错PNG'); header = true; }
    if (type === 'IDAT') compressed.push(bytes.subarray(offset + 8, offset + 8 + size));
    if (type === 'IEND') { if (size !== 0 || end !== bytes.length) fail(400, 'PNG结束块无效'); ended = true; break; }
    offset = end;
  }
  if (!ended || !compressed.length) fail(400, 'PNG缺少像素或结束块');
  const channels = ({ 0: 1, 2: 3, 4: 2, 6: 4 })[bytes[25]], row = width * channels + 1;
  try { const raw = zlib.inflateSync(Buffer.concat(compressed), { maxOutputLength: row * height }); if (raw.length !== row * height) fail(400, 'PNG像素尺寸不一致'); for (let y=0;y<height;y++) if(raw[y*row]>4) fail(400,'PNG像素滤镜无效'); }
  catch (error) { if(error.status)throw error; fail(400, 'PNG像素数据无效'); }
  return bytes.toString('base64');
}
const publicUser = user => ({ id: user.id, name: user.name, code: user.code });
const publicEntry = item => ({ ...item, likes: item.likes?.length ?? 0, liked: false });

/** Dependency-free local demo backend. All mutations persist before replacing the live snapshot. */
export function createAtelierService({ dataFile, host = '127.0.0.1', port = 18747 } = {}) {
  if (!dataFile) throw new Error('An explicit dataFile is required');
  if (host !== '127.0.0.1' && host !== '::1' && host !== 'localhost') throw new Error('This demo service only binds loopback');
  dataFile = path.resolve(dataFile); fs.mkdirSync(path.dirname(dataFile), { recursive: true });
  if (fs.existsSync(dataFile) && fs.statSync(dataFile).size > MAX_STORE) throw new Error('Social store exceeds 64MB; original file retained');
  let db = fs.existsSync(dataFile) ? JSON.parse(fs.readFileSync(dataFile, 'utf8')) : {
    schemaVersion: 1, adminKey: crypto.randomBytes(32).toString('hex'), users: [], friends: [], entries: [], reports: []
  };
  if (db.schemaVersion !== 1 || !Array.isArray(db.users) || !Array.isArray(db.entries) || !db.adminKey) throw new Error('Unsupported social store; original file retained');
  function persist(candidate) {
    const temporary = dataFile + '.' + crypto.randomUUID() + '.tmp';
    const serialized = JSON.stringify(candidate, null, 2);
    if (Buffer.byteLength(serialized) > MAX_STORE) fail(409, '本机邮局达到64MB容量，请先备份整理');
    fs.writeFileSync(temporary, serialized, { mode: 0o600 });
    fs.renameSync(temporary, dataFile);
  }
  if (!fs.existsSync(dataFile)) persist(db);
  const adminFile = path.join(path.dirname(dataFile), 'moderator-key.txt');
  if (!fs.existsSync(adminFile)) fs.writeFileSync(adminFile, db.adminKey + '\n', { mode: 0o600 });
  function mutate(action) { const candidate = structuredClone(db); const result = action(candidate); persist(candidate); db = candidate; return result; }
  const limited = new Map();
  function rate(key, max = 300) {
    const now = Date.now(); const record = limited.get(key);
    if (!record || now - record.start > 60_000) { limited.set(key, { start: now, count: 1 }); return; }
    if (++record.count > max) fail(429, '稍等片刻再试');
    if (limited.size > 1000) for (const [entry, r] of limited) if (now - r.start > 60_000) limited.delete(entry);
  }
  const userFor = request => {
    const token = String(request.headers.authorization ?? '').replace(/^Bearer /u, '');
    if (!token || token.length > 200) fail(401, '请先注册或连接本机身份');
    const user = db.users.find(item => item.tokenHash === digest(token));
    if (!user || user.banned) fail(403, '身份无效或账号被暂停');
    rate(user.id); return user;
  };
  const adminFor = request => {
    const provided = String(request.headers['x-admin-key'] ?? '');
    if (!provided || digest(provided) !== digest(db.adminKey)) fail(403, '需要独立审核密钥');
  };
  const blocked = (a, b) => a.blocks.includes(b.id) || b.blocks.includes(a.id);
  function approvedFor(item, user) {
    const author = db.users.find(u => u.id === item.ownerId);
    return item.status === 'approved' && author && !author.banned && !blocked(user, author);
  }
  const entryView = (entry, user) => ({ ...publicEntry(entry), liked: entry.likes?.includes(user.id) ?? false });
  function createEntry(candidate, user, body, kind, extra = {}) {
    const key = clean(body.requestKey ?? crypto.randomUUID(), 100);
    const previous = candidate.entries.find(e => e.ownerId === user.id && e.requestKey === key);
    if (previous) return previous;
    if (candidate.entries.length >= 5000) fail(409, '本机邮局容量已满，请先备份和整理');
    const item = { id: crypto.randomUUID(), ownerId: user.id, ownerName: user.name, kind, title: clean(body.title, 80), text: clean(body.text ?? '', 800, 0),
      pngBase64: png(body.pngBase64, kind === 'commission' || kind === 'letter'), status: 'pending', createdUtc: new Date().toISOString(),
      requestKey: key, likes: [], acked: false, ...extra };
    candidate.entries.push(item); return item;
  }
  async function bodyFor(request) {
    if (!String(request.headers['content-type'] ?? '').startsWith('application/json')) fail(415, '只接受application/json');
    const chunks = []; let bytes = 0;
    for await (const chunk of request) { bytes += chunk.length; if (bytes > MAX_BODY) fail(413, '内容过大'); chunks.push(chunk); }
    try { const value = JSON.parse(Buffer.concat(chunks).toString('utf8')); if (!value || Array.isArray(value) || typeof value !== 'object') fail(400, 'JSON对象无效'); return value; }
    catch (error) { if (error.status) throw error; fail(400, 'JSON格式无效'); }
  }
  async function route(request) {
    if (!['GET', 'POST'].includes(request.method)) fail(405, '只允许GET和POST');
    const hostHeader = String(request.headers.host ?? '');
    if (!/^(?:127\.0\.0\.1|localhost|\[::1\])(?::\d+)?$/u.test(hostHeader)) fail(403, '只接受本机Host');
    if (request.headers.origin && request.headers.origin !== 'http://' + hostHeader) fail(403, '不接受跨站请求');
    const url = new URL(request.url, 'http://127.0.0.1'); const segments = url.pathname.split('/').filter(Boolean);
    const method = request.method;
    if (method === 'GET' && url.pathname === '/health') return { ok: true, localOnly: true, schemaVersion: 1 };
    if (segments[0] !== 'v1') fail(404, '没有这个入口');
    if (method === 'POST' && segments[1] === 'register') {
      rate('register', 24); const body = await bodyFor(request); const name = clean(body.name, 24);
      if (db.users.length >= 1000) fail(409, '本机身份数量已达上限');
      const token = crypto.randomBytes(32).toString('hex');
      const user = mutate(candidate => { const user = { id: crypto.randomUUID(), name, code: crypto.randomBytes(5).toString('hex').toUpperCase(), tokenHash: digest(token), blocks: [], banned: false }; candidate.users.push(user); return user; });
      return { ...publicUser(user), token };
    }
    if (segments[1] === 'moderation') {
      adminFor(request);
      if (method === 'GET') return { entries: db.entries.filter(e => e.status === 'pending' || e.status === 'quarantined').slice(-40).map(publicEntry), reports: db.reports.slice(-40), users: db.users.map(u => ({ ...publicUser(u), banned: u.banned })) };
      const body = await bodyFor(request);
      if (segments[2] === 'users') {
        return mutate(candidate => { const user = candidate.users.find(u => u.id === segments[3]); if (!user) fail(404, '账号不存在'); user.banned = body.blocked === true; return { ok: true }; });
      }
      if (!['approve', 'reject'].includes(body.decision)) fail(400, '审核决定无效');
      return mutate(candidate => { const item = candidate.entries.find(e => e.id === segments[2]); if (!item) fail(404, '内容不存在'); item.status = body.decision === 'approve' ? 'approved' : 'rejected'; item.moderatedUtc = new Date().toISOString();
        if (item.kind === 'fulfillment' && item.status === 'approved') { const parent = candidate.entries.find(e => e.id === item.parentId); if (parent) parent.fulfilledId = item.id; }
        return publicEntry(item); });
    }
    const user = userFor(request); const action = segments[1], id = segments[2];
    if (method === 'GET' && action === 'me') return { ...publicUser(user), blocks: user.blocks };
    if (action === 'gallery') {
      if (method === 'GET') return { entries: db.entries.filter(e => ['page', 'fulfillment'].includes(e.kind) && approvedFor(e, user)).slice(-24).reverse().map(e => entryView(e, user)) };
      const body = await bodyFor(request);
      if (!id) return mutate(candidate => publicEntry(createEntry(candidate, user, body, 'page')));
      if (segments[3] === 'like') return mutate(candidate => { const item = candidate.entries.find(e => e.id === id); if (!item || !approvedFor(item, user)) fail(404, '作品不可见'); if (!item.likes.includes(user.id)) item.likes.push(user.id); return { ok: true, likes: item.likes.length, liked: true }; });
    }
    if (method === 'GET' && action === 'mine') return { entries: db.entries.filter(e => e.ownerId === user.id).slice(-40).reverse().map(e => entryView(e, user)) };
    if (action === 'friends') {
      if (method === 'GET') return { users: db.friends.filter(f => f.status === 'accepted' && [f.from, f.to].includes(user.id)).map(f => db.users.find(u => u.id === (f.from === user.id ? f.to : f.from))).filter(u => u && !u.banned && !blocked(user, u)).map(publicUser), requests: db.friends.filter(f => f.to === user.id && f.status === 'pending').map(f => ({ id: f.id, from: publicUser(db.users.find(u => u.id === f.from)) })) };
      const body = await bodyFor(request);
      if (segments[2] === 'accept') return mutate(candidate => { const invite = candidate.friends.find(f => f.id === body.requestId && f.to === user.id); if (!invite) fail(404, '邀请不存在'); invite.status = 'accepted'; return { ok: true }; });
      const other = db.users.find(u => u.code === body.code); if (!other || other.id === user.id || other.banned || blocked(user, other)) fail(400, '好友码无效或不可邀请');
      return mutate(candidate => { let invite = candidate.friends.find(f => [f.from, f.to].includes(user.id) && [f.from, f.to].includes(other.id)); if (!invite) { invite = { id: crypto.randomUUID(), from: user.id, to: other.id, status: 'pending' }; candidate.friends.push(invite); } return invite; });
    }
    if (method === 'POST' && action === 'letters') {
      const body = await bodyFor(request); const recipient = db.users.find(u => u.id === body.recipientId);
      if (!recipient || recipient.banned || blocked(user, recipient) || !db.friends.some(f => f.status === 'accepted' && [f.from, f.to].includes(user.id) && [f.from, f.to].includes(recipient.id))) fail(403, '只能寄给已接受邀请的好友');
      return mutate(candidate => publicEntry(createEntry(candidate, user, body, 'letter', { recipientId: recipient.id })));
    }
    if (action === 'inbox') {
      if (method === 'GET') return { entries: db.entries.filter(e => e.recipientId === user.id && approvedFor(e, user)).slice(-40).reverse().map(e => entryView(e, user)) };
      if (method === 'POST' && segments[3] === 'ack') return mutate(candidate => { const item = candidate.entries.find(e => e.id === id && e.recipientId === user.id && e.status === 'approved'); if (!item) fail(404, '回礼不存在'); item.acked = true; return { ok: true, id: item.id }; });
    }
    if (action === 'commissions') {
      if (method === 'GET') return { entries: db.entries.filter(e => e.kind === 'commission' && approvedFor(e, user)).slice(-30).reverse().map(e => entryView(e, user)) };
      const body = await bodyFor(request);
      if (!id) return mutate(candidate => publicEntry(createEntry(candidate, user, body, 'commission', { acceptedBy: '', fulfilledId: '' })));
      const original = db.entries.find(e => e.id === id && e.kind === 'commission');
      if (!original || !approvedFor(original, user) || original.ownerId === user.id) fail(404, '委托不存在或不能接自己的委托');
      if (segments[3] === 'accept') return mutate(candidate => { const item = candidate.entries.find(e => e.id === id); if (item.acceptedBy && item.acceptedBy !== user.id) fail(409, '已有其他朋友接下委托'); item.acceptedBy = user.id; return publicEntry(item); });
      if (segments[3] === 'submit') {
        if (original.acceptedBy !== user.id || original.fulfilledId) fail(409, '先接下委托，且每个委托只提交一次');
        if (db.entries.some(e => e.parentId === id && e.status !== 'rejected')) fail(409, '作品已在审核或已寄达');
        return mutate(candidate => publicEntry(createEntry(candidate, user, body, 'fulfillment', { parentId: id, recipientId: original.ownerId })));
      }
    }
    if (method === 'POST' && action === 'reports') {
      const body = await bodyFor(request); const reason = clean(body.reason, 240); const item = db.entries.find(e => e.id === body.contentId);
      if (!item || !(approvedFor(item, user) || item.ownerId === user.id)) fail(404, '内容不存在');
      return mutate(candidate => { const old = candidate.reports.find(r => r.reporterId === user.id && r.contentId === item.id); if (!old) candidate.reports.push({ id: crypto.randomUUID(), reporterId: user.id, contentId: item.id, reason, createdUtc: new Date().toISOString() }); candidate.entries.find(e => e.id === item.id).status = 'quarantined'; return { ok: true, hiddenPendingReview: true }; });
    }
    if (method === 'POST' && action === 'block') {
      const body = await bodyFor(request); if (body.playerId === user.id || !db.users.some(u => u.id === body.playerId)) fail(400, '账号无效');
      return mutate(candidate => { const current = candidate.users.find(u => u.id === user.id); if (!current.blocks.includes(body.playerId)) current.blocks.push(body.playerId); return { ok: true }; });
    }
    fail(404, '没有这个操作');
  }
  const server = http.createServer(async (request, response) => {
    response.setHeader('Content-Type', 'application/json; charset=utf-8'); response.setHeader('Cache-Control', 'no-store');
    response.setHeader('X-Content-Type-Options', 'nosniff');
    try { const result = await route(request); response.writeHead(200); response.end(JSON.stringify(result)); }
    catch (error) { const status = error.status ?? 500; response.writeHead(status); response.end(JSON.stringify({ error: status === 500 ? '保存未完成，原数据保留，请重试' : error.message })); }
  });
  server.requestTimeout = 10_000; server.headersTimeout = 8_000;
  return { server, adminKey: db.adminKey, dataFile, listen: () => new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, host, () => { server.removeListener('error', reject); resolve(server.address()); }); }), close: () => new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())) };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const option = (name, fallback) => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : fallback; };
  const service = createAtelierService({ dataFile: option('--data', path.join(path.dirname(fileURLToPath(import.meta.url)), 'data', 'social-store.json')), port: Number(option('--port', '18747')) });
  const address = await service.listen();
  console.log(`Atelier local post office listening at http://127.0.0.1:${address.port}`);
  console.log('Moderation key is stored in moderator-key.txt beside the store. Never publish that file.');
  for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, async () => { await service.close(); process.exit(0); });
}
