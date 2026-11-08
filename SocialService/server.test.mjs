import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { createAtelierService } from './server.mjs';

function fixturePng() {
  function chunk(type, data) { const kind=Buffer.from(type), payload=Buffer.concat([kind,data]);let crc=0xffffffff;for(const b of payload){crc^=b;for(let i=0;i<8;i++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}const size=Buffer.alloc(4),sum=Buffer.alloc(4);size.writeUInt32BE(data.length);sum.writeUInt32BE((crc^0xffffffff)>>>0);return Buffer.concat([size,payload,sum]); }
  const header=Buffer.alloc(13);header.writeUInt32BE(1,0);header.writeUInt32BE(1,4);header[8]=8;header[9]=6;
  return Buffer.concat([Buffer.from('89504e470d0a1a0a','hex'),chunk('IHDR',header),chunk('IDAT',zlib.deflateSync(Buffer.from([0,180,220,250,255]))),chunk('IEND',Buffer.alloc(0))]).toString('base64');
}
const image = fixturePng();
test('Real two-client local HTTP, moderation, gifts, commissions and persisted-store reopen', async t => {
  const testRoot = path.resolve('tmp'); fs.mkdirSync(testRoot, { recursive: true });
  const folder = fs.mkdtempSync(path.join(testRoot, 'atelier-social-test-'));
  const dataFile = path.join(folder, 'social-store.json');
  let service = createAtelierService({ dataFile, port: 0 }); let address = await service.listen();
  let base = `http://127.0.0.1:${address.port}`;
  async function request(method, endpoint, body, token, admin) {
    const headers = { 'Content-Type': 'application/json' }; if (token) headers.Authorization = 'Bearer ' + token; if (admin) headers['X-Admin-Key'] = admin;
    const response = await fetch(base + endpoint, { method, headers, ...(body ? { body: JSON.stringify(body) } : {}) });
    return { status: response.status, body: await response.json() };
  }
  try {
    let alice, bob, page, invite, gift, commission, submitted;
    await t.test('Health is loopback-only; registration returns independent identities', async () => {
      assert.equal((await request('GET', '/health')).body.localOnly, true);
      alice = (await request('POST', '/v1/register', { name: '云间阿狸' })).body;
      bob = (await request('POST', '/v1/register', { name: '月台小鹿' })).body;
      assert.notEqual(alice.id, bob.id); assert.notEqual(alice.token, bob.token); assert.ok(alice.code);
      assert.equal((await request('GET', '/v1/gallery')).status, 401);
    });
    await t.test('Publication is explicit, pending and idempotent, invisible to other client', async () => {
      page = (await request('POST', '/v1/gallery', { title: '一页月光', text: '给迷路的星星', pngBase64: image, requestKey: 'publish-1' }, alice.token)).body;
      assert.equal(page.status, 'pending',JSON.stringify(page));
      assert.equal((await request('GET', '/v1/gallery', null, bob.token)).body.entries.length, 0);
      assert.equal((await request('POST','/v1/reports',{contentId:page.id,reason:'不能举报别人的未公开草稿'},bob.token)).status,404);
      assert.equal((await request('POST', '/v1/gallery', { title: '重试', text: '', pngBase64: image, requestKey: 'publish-1' }, alice.token)).body.id, page.id);
      assert.equal((await request('GET', '/v1/mine', null, alice.token)).body.entries.length, 1);
    });
    await t.test('Ordinary user cannot moderate; independent admin approval reveals gallery', async () => {
      assert.equal((await request('POST', '/v1/moderation/' + page.id, { decision: 'approve' }, alice.token)).status, 403);
      assert.equal((await request('POST', '/v1/moderation/' + page.id, { decision: 'approve' }, null, service.adminKey)).status, 200);
      assert.equal((await request('GET', '/v1/gallery', null, bob.token)).body.entries[0].id, page.id);
    });
    await t.test('Likes are idempotent, and there is no comments endpoint', async () => {
      await request('POST', '/v1/gallery/' + page.id + '/like', {}, bob.token);
      assert.equal((await request('POST', '/v1/gallery/' + page.id + '/like', {}, bob.token)).body.likes, 1);
      assert.equal((await request('POST', '/v1/comments', { text: 'x' }, bob.token)).status, 404);
    });
    await t.test('Untrusted remote URLs, malformed PNGs and oversized text are rejected', async () => {
      assert.equal((await request('POST', '/v1/gallery', { title: 'x', pngBase64: 'https://example.invalid/image.png' }, alice.token)).status, 400);
      assert.equal((await request('POST', '/v1/gallery', { title: 'x', pngBase64: Buffer.from('not a PNG').toString('base64') }, alice.token)).status, 400);
      assert.equal((await request('POST', '/v1/gallery', { title: 'x', pngBase64: Buffer.from(image,'base64').subarray(0,33).toString('base64') }, alice.token)).status, 400);
      assert.equal((await request('POST', '/v1/commissions', { title: 'x', text: 'x'.repeat(801) }, alice.token)).status, 400);
      assert.equal((await request('DELETE', '/v1/gallery', null, alice.token)).status, 405);
      assert.equal((await fetch(base+'/v1/gallery',{headers:{Authorization:'Bearer '+alice.token,Origin:'https://evil.invalid'}})).status,403);
    });
    await t.test('Friend invitation requires acceptance before gift delivery', async () => {
      assert.equal((await request('POST', '/v1/letters', { recipientId: bob.id, title: 'x', text: 'x' }, alice.token)).status, 403);
      invite = (await request('POST', '/v1/friends', { code: bob.code }, alice.token)).body;
      assert.equal((await request('GET', '/v1/friends', null, bob.token)).body.requests[0].id, invite.id);
      await request('POST', '/v1/friends/accept', { requestId: invite.id }, bob.token);
      assert.equal((await request('GET', '/v1/friends', null, alice.token)).body.users[0].id, bob.id);
    });
    await t.test('Gift is moderated before recipient sees it; ack is idempotent and private', async () => {
      gift = (await request('POST', '/v1/letters', { recipientId: bob.id, title: '小小月亮', text: '送你一枚自己画的贴纸', pngBase64: image, requestKey: 'gift-1' }, alice.token)).body;
      assert.equal((await request('GET', '/v1/inbox', null, bob.token)).body.entries.length, 0);
      await request('POST', '/v1/moderation/' + gift.id, { decision: 'approve' }, null, service.adminKey);
      assert.equal((await request('GET', '/v1/inbox', null, bob.token)).body.entries[0].id, gift.id);
      assert.equal((await request('POST', '/v1/inbox/' + gift.id + '/ack', {}, alice.token)).status, 404);
      await request('POST', '/v1/inbox/' + gift.id + '/ack', {}, bob.token);
      assert.equal((await request('POST', '/v1/inbox/' + gift.id + '/ack', {}, bob.token)).body.ok, true);
      assert.equal((await request('GET', '/v1/inbox', null, bob.token)).body.entries.length, 1);
      assert.equal((await request('GET', '/v1/inbox', null, bob.token)).body.entries[0].acked, true);
      assert.equal((await request('GET', '/v1/inbox?recipientId='+bob.id, null, alice.token)).body.entries.length,0);
    });
    await t.test('Player commission requires moderation, acceptance and reviewed photo fulfillment', async () => {
      commission = (await request('POST', '/v1/commissions', { title: '请留一页夏末', text: '想要温暖又舍不得的感觉', requestKey: 'commission-1' }, alice.token)).body;
      assert.equal((await request('GET', '/v1/commissions', null, bob.token)).body.entries.length, 0);
      await request('POST', '/v1/moderation/' + commission.id, { decision: 'approve' }, null, service.adminKey);
      assert.equal((await request('POST', '/v1/commissions/' + commission.id + '/accept', {}, alice.token)).status, 404);
      await request('POST', '/v1/commissions/' + commission.id + '/accept', {}, bob.token);
      submitted = (await request('POST', '/v1/commissions/' + commission.id + '/submit', { title: '给你的夏末', text: '一小片星光', pngBase64: image, requestKey: 'submit-1' }, bob.token)).body;
      assert.equal(submitted.status, 'pending');
      assert.equal((await request('POST', '/v1/commissions/' + commission.id + '/submit', { title: 'x', pngBase64: image }, bob.token)).status, 409);
      await request('POST', '/v1/moderation/' + submitted.id, { decision: 'approve' }, null, service.adminKey);
      assert.equal((await request('GET', '/v1/commissions', null, alice.token)).body.entries[0].fulfilledId, submitted.id);
      assert.ok((await request('GET', '/v1/inbox', null, alice.token)).body.entries.some(e => e.id === submitted.id));
    });
    await t.test('Reports immediately quarantine content; reviewer can reject it', async () => {
      await request('POST', '/v1/reports', { contentId: page.id, reason: '测试举报审核流程' }, bob.token);
      assert.ok(!(await request('GET', '/v1/gallery', null, bob.token)).body.entries.some(e => e.id === page.id));
      const review = (await request('GET', '/v1/moderation', null, null, service.adminKey)).body;
      assert.ok(review.reports.some(r => r.contentId === page.id));
      await request('POST', '/v1/moderation/' + page.id, { decision: 'reject' }, null, service.adminKey);
      assert.equal((await request('GET', '/v1/mine', null, alice.token)).body.entries.find(e => e.id === page.id).status, 'rejected');
    });
    await t.test('Store restart retains identities, reviewed content, gifts and claims without exposing tokens', async () => {
      const key = service.adminKey; await service.close();
      const raw = fs.readFileSync(dataFile, 'utf8'); assert.ok(!raw.includes(alice.token)); assert.ok(!raw.includes(bob.token));
      service = createAtelierService({ dataFile, port: 0 }); address = await service.listen(); base = `http://127.0.0.1:${address.port}`;
      assert.equal(service.adminKey, key);
      assert.equal((await request('GET', '/v1/me', null, alice.token)).body.id, alice.id);
      assert.equal((await request('GET', '/v1/inbox', null, bob.token)).body.entries.find(e => e.id === gift.id).acked, true);
      assert.equal((await request('GET', '/v1/commissions', null, bob.token)).body.entries[0].fulfilledId, submitted.id);
    });
    await t.test('User block suppresses content/gifts and administrator ban denies requests', async () => {
      await request('POST', '/v1/block', { playerId: alice.id }, bob.token);
      assert.equal((await request('GET', '/v1/inbox', null, bob.token)).body.entries.length, 0);
      assert.equal((await request('POST', '/v1/letters', { recipientId: bob.id, title: 'x', text: 'x' }, alice.token)).status, 403);
      await request('POST', '/v1/moderation/users/' + alice.id, { blocked: true }, null, service.adminKey);
      assert.equal((await request('GET', '/v1/me', null, alice.token)).status, 403);
    });
    await t.test('Non-loopback bind is denied before opening any listener', () => {
      assert.throws(() => createAtelierService({ dataFile, host: '0.0.0.0', port: 0 }), /loopback/);
    });
    await t.test('An occupied port rejects listen cleanly', async()=>{
      const duplicate=createAtelierService({dataFile,port:address.port});
      await assert.rejects(duplicate.listen(),{code:'EADDRINUSE'});
    });
  } finally { await service.close(); if(!folder.startsWith(testRoot+path.sep))throw new Error('Unexpected cleanup target'); fs.rmSync(folder, { recursive: true, force: true }); }
});
