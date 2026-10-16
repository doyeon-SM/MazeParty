import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync, createHash, verify } from 'node:crypto';
import { BridgeError, createBridge, parseUpstreamJson } from './bridge.mjs';
import { makeServer } from './server.mjs';

const issuer = generateKeyPairSync('rsa', { modulusLength: 2048 });
const client = generateKeyPairSync('rsa', { modulusLength: 2048 });
const modulus = Buffer.from(client.publicKey.export({ format: 'jwk' }).n, 'base64url').toString('base64');
const input = { stoveAccessToken: 'user-token', publicKey: modulus };
const config = {
  stoveBaseUrl: 'https://api.onstove.com', stoveGameId: 'MAZEPARTY', stoveClientId: 'server-client', stoveClientSecret: 'server-secret',
  unityProjectId: '11111111-1111-1111-1111-111111111111', unityEnvironmentId: '22222222-2222-2222-2222-222222222222',
  unityEnvironment: 'stove-test', unityKeyId: 'unity-key', unityKeySecret: 'unity-secret',
  signingKey: issuer.privateKey.export({ type: 'pkcs8', format: 'pem' })
};
function upstream(verifyResult = '{"code":0,"value":{"guid":18446744073709551614}}', calls = []) {
  return async (url, options) => {
    calls.push({ url, options });
    if (url.endsWith('/server_token')) return Response.json({ code: 0, response_data: { access_token: 'server-only', expires_in: 3600 } });
    if (url.endsWith('/token/verify')) return new Response(verifyResult);
    if (url.includes('token-exchange')) return Response.json({ accessToken: 'unity-server-only' });
    if (url.endsWith('/custom-id')) return Response.json({ userId: 'player-a', idToken: 'player-access', sessionToken: 'player-session' });
    throw new Error('Unexpected request');
  };
}

test('verified STOVE ID alone determines a new scoped UGS account and signed key-bound grant', async () => {
  const calls = [];
  const auth = createBridge(config, { fetchImpl: upstream(undefined, calls), now: () => 1800000000000 });
  const result = await auth(input);
  assert.equal(result.playerId, 'player-a');
  assert.equal(result.environment, 'stove-test');
  assert.equal(result.grantBody, 'mpg1|stove|stove-test|player-a|' +
    createHash('sha256').update(Buffer.from(modulus, 'base64')).digest('hex') + '|1800000600');
  assert.ok(verify('RSA-SHA256', Buffer.from(result.grantBody), issuer.publicKey, Buffer.from(result.grantSignature, 'base64')));
  const custom = calls.find(call => call.url.endsWith('/custom-id'));
  assert.deepEqual(JSON.parse(custom.options.body), { externalId: 'stove-' + createHash('sha256')
    .update('mazeparty-release-v1|MAZEPARTY|stove-test|18446744073709551614').digest('hex') });
  assert.equal(custom.options.headers.UnityEnvironment, 'stove-test');
  assert.ok(!JSON.stringify(result).includes('server-only'));
  await auth(input);
  assert.equal(calls.filter(call => call.url.endsWith('/token/verify')).length, 2, 'Always revalidate user token');
  assert.equal(calls.filter(call => call.url.endsWith('/server_token')).length, 1, 'Only server token is cached');
});

test('unverified, malformed and caller-selected identities never reach UGS', async () => {
  for (const response of ['{"code":40000}', '{"code":0,"value":{"guid":0}}',
    '{"code":0,"value":{"guid":18446744073709551616}}', '{"code":0,"value":{"member_no":"123"}}']) {
    const calls = [];
    const auth = createBridge(config, { fetchImpl: upstream(response, calls) });
    await assert.rejects(auth(input), BridgeError);
    assert.equal(calls.filter(call => call.url.includes('unity.com')).length, 0);
  }
  for (const body of [{ ...input, playerId: 'victim' }, { ...input, accessToken: 'legacy-ugs-token' },
    { ...input, publicKey: Buffer.alloc(256).toString('base64') }, { ...input, stoveAccessToken: '' }]) {
    let called = false;
    const auth = createBridge(config, { fetchImpl: async () => { called = true; throw Error(); } });
    await assert.rejects(auth(body), error => error.status === 400);
    assert.equal(called, false);
  }
  assert.equal(parseUpstreamJson('{"guid":9007199254740993}').guid, '9007199254740993');
});

test('expired STOVE server token is renewed once; upstream failures are sanitized', async () => {
  const calls = [];
  const valid = upstream(undefined, calls);
  let verified = 0;
  const auth = createBridge(config, { fetchImpl: async (url, options) => {
    if (url.endsWith('/token/verify') && ++verified === 1) return Response.json({ code: 40103 });
    return valid(url, options);
  } });
  assert.equal((await auth(input)).playerId, 'player-a');
  assert.equal(calls.filter(call => call.url.endsWith('/server_token')).length, 2);
  for (const fetchImpl of [async () => { throw Error('secret-response'); },
    async () => new Response('x'.repeat(65537)), async () => new Response('{bad-json')]) {
    await assert.rejects(createBridge(config, { fetchImpl })(input), error =>
      error.status === 503 && error.message === 'authentication_unavailable');
  }
});

test('HTTP rejects body abuse, spoofed proxy headers and rate floods without exposing tokens', async () => {
  let calls = 0;
  const server = makeServer(async () => { calls++; throw Error('secret-server-token'); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = 'http://127.0.0.1:' + server.address().port + '/v1/stove/auth';
  try {
    let response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
    assert.equal(response.status, 503);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    assert.equal(await response.text(), '{"error":"authentication_unavailable"}');
    response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: 'x'.repeat(12289) });
    assert.equal(response.status, 400);
    assert.equal(calls, 1);
    for (let i = 0; i < 29; i++) response = await fetch(url, { method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Forwarded-For': '1.2.3.' + i }, body: '{}' });
    assert.equal(response.status, 429);
  } finally { await new Promise(resolve => server.close(resolve)); }
});
