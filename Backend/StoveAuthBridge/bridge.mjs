import { createHash, createPrivateKey, createPublicKey, sign } from 'node:crypto';

export class BridgeError extends Error {
  constructor(status, code) { super(code); this.status = status; this.code = code; }
}
const unavailable = () => new BridgeError(503, 'authentication_unavailable');

// Keep unsigned 64-bit STOVE identifiers exact, even when sent as JSON numbers.
export function parseUpstreamJson(text) {
  return JSON.parse(text, (key, value, context) => {
    if ((key === 'guid' || key === 'member_no') && typeof value === 'number') {
      if (!context?.source || !/^[1-9][0-9]{0,19}$/.test(context.source)) throw unavailable();
      return context.source;
    }
    return value;
  });
}

export function validateConfig(config) {
  for (const field of ['stoveBaseUrl', 'stoveGameId', 'stoveClientId', 'stoveClientSecret',
    'unityProjectId', 'unityEnvironmentId', 'unityEnvironment', 'unityKeyId', 'unityKeySecret', 'signingKey'])
    if (!config[field]) throw new Error('Missing server configuration: ' + field);
  const base = new URL(config.stoveBaseUrl);
  if (base.protocol !== 'https:' || base.username || base.password || base.search || base.hash ||
      base.pathname !== '/' || !base.hostname.endsWith('.onstove.com'))
    throw new Error('STOVE API origin must be the HTTPS onstove.com environment issued for this game.');
  if (!/^stove-[a-z0-9-]{1,40}$/.test(config.unityEnvironment) ||
      !/^[a-zA-Z0-9_-]{1,100}$/.test(config.stoveGameId)) throw new Error('Invalid game or environment.');
  if (config.stoveIdField && !['guid', 'member_no'].includes(config.stoveIdField))
    throw new Error('STOVE_ID_FIELD must be guid or member_no.');
  for (const field of ['unityProjectId', 'unityEnvironmentId'])
    if (!/^[a-f0-9-]{36}$/i.test(config[field])) throw new Error('Invalid UUID: ' + field);
  const key = createPrivateKey(config.signingKey);
  const jwk = createPublicKey(key).export({ format: 'jwk' });
  if (key.asymmetricKeyType !== 'rsa' || Buffer.from(jwk.n, 'base64url').length !== 256 || jwk.e !== 'AQAB')
    throw new Error('Use an RSA 2048-bit signing key with exponent 65537.');
  return key;
}

export function createBridge(config, { fetchImpl = fetch, now = () => Date.now() } = {}) {
  const signingKey = validateConfig(config);
  let stoveServerToken, unityServerToken;
  let stoveTokenRequest, unityTokenRequest;
  const seconds = () => Math.floor(now() / 1000);

  async function post(url, headers, body, allowStoveError = false) {
    try {
      const response = await fetchImpl(url, { method: 'POST', redirect: 'error',
        signal: AbortSignal.timeout(5000), headers: { 'Content-Type': 'application/json', ...headers },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
      if (Number(response.headers.get('content-length')) > 65536) throw unavailable();
      const reader = response.body.getReader();
      const chunks = []; let size = 0;
      try {
        while (true) {
          const { done, value } = await reader.read(); if (done) break;
          size += value.byteLength;
          if (size > 65536) { await reader.cancel(); throw unavailable(); }
          chunks.push(value);
        }
      } finally { reader.releaseLock(); }
      const result = parseUpstreamJson(Buffer.concat(chunks).toString('utf8'));
      if (!response.ok && !(allowStoveError && Number.isInteger(result.code))) throw unavailable();
      return result;
    } catch { throw unavailable(); }
  }

  async function stoveToken() {
    if (stoveServerToken?.until > seconds()) return stoveServerToken.value;
    if (stoveTokenRequest) return stoveTokenRequest;
    stoveTokenRequest = (async () => {
      const response = await post(config.stoveBaseUrl + '/auth/v5/server_token',
        { 'caller-id': config.stoveGameId + '_SERVER' }, {
          client_id: config.stoveClientId, client_secret: config.stoveClientSecret,
          service_id: config.stoveGameId, instance_id: config.instanceId || 'mazeparty-auth'
        });
      const token = response.response_data?.access_token;
      if (response.code !== 0 || typeof token !== 'string' || !token) throw unavailable();
      // The server token alone is cached. Refresh at least every 12 hours;
      // STOVE rejection also invalidates it. User tokens are never cached.
      const lifetime = Number(response.response_data.expires_in);
      const ttl = Number.isFinite(lifetime) && lifetime > 60 ? Math.min(lifetime * 0.7, 43200) : 300;
      stoveServerToken = { value: token, until: seconds() + ttl };
      return token;
    })();
    try { return await stoveTokenRequest; } finally { stoveTokenRequest = undefined; }
  }

  async function verifyStove(token) {
    for (let attempt = 0; attempt < 2; attempt++) {
      const result = await post(config.stoveBaseUrl + '/member/v3.0/' + config.stoveGameId + '/token/verify',
        { Authorization: 'Bearer ' + await stoveToken(), 'caller-id': config.stoveGameId + '_SERVER' },
        { access_token: token }, true);
      if ((result.code === 40101 || result.code === 40103) && attempt === 0) {
        stoveServerToken = undefined; continue;
      }
      if (result.code !== 0) throw new BridgeError(401, 'platform_login_required');
      // Choose one identifier field per deployment. Do not silently alternate
      // between GUID and member number, which would split returning accounts.
      const id = result.value?.[config.stoveIdField || 'guid'];
      if (typeof id !== 'string' || !/^[1-9][0-9]{0,19}$/.test(id) || BigInt(id) > 18446744073709551615n)
        throw unavailable();
      return id;
    }
    throw unavailable();
  }

  async function unityToken() {
    if (unityServerToken?.until > seconds()) return unityServerToken.value;
    if (unityTokenRequest) return unityTokenRequest;
    unityTokenRequest = (async () => {
      const query = new URLSearchParams({ projectId: config.unityProjectId, environmentId: config.unityEnvironmentId });
      const result = await post('https://services.api.unity.com/auth/v2/token-exchange?' + query,
        { Authorization: 'Basic ' + Buffer.from(config.unityKeyId + ':' + config.unityKeySecret).toString('base64') });
      if (typeof result.accessToken !== 'string' || !result.accessToken) throw unavailable();
      unityServerToken = { value: result.accessToken, until: seconds() + 300 };
      return result.accessToken;
    })();
    try { return await unityTokenRequest; } finally { unityTokenRequest = undefined; }
  }

  return async function authenticate(input) {
    if (!input || typeof input !== 'object' || Array.isArray(input) ||
        Object.keys(input).some(key => key !== 'stoveAccessToken' && key !== 'publicKey') ||
        typeof input.stoveAccessToken !== 'string' || input.stoveAccessToken.length < 1 || input.stoveAccessToken.length > 8192 ||
        typeof input.publicKey !== 'string' || !/^[A-Za-z0-9+/]{342}==$/.test(input.publicKey))
      throw new BridgeError(400, 'invalid_request');
    const modulus = Buffer.from(input.publicKey, 'base64');
    if (modulus.length !== 256 || modulus[0] < 128 || !(modulus[255] & 1) || modulus.toString('base64') !== input.publicKey)
      throw new BridgeError(400, 'invalid_key');
    const id = await verifyStove(input.stoveAccessToken);
    const identity = await post('https://player-auth.services.api.unity.com/v1/projects/' +
      config.unityProjectId + '/authentication/server/custom-id',
      { Authorization: 'Bearer ' + await unityToken(), UnityEnvironment: config.unityEnvironment },
      // Hash a fixed namespace, game, environment and authoritative ID. Never
      // accept an existing UGS token or a caller-selected account ID for linking.
      { externalId: 'stove-' + createHash('sha256').update(
        ['mazeparty-release-v1', config.stoveGameId, config.unityEnvironment, id].join('|')).digest('hex') });
    if (typeof identity.userId !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(identity.userId) ||
        typeof identity.idToken !== 'string' || !identity.idToken ||
        typeof identity.sessionToken !== 'string' || !identity.sessionToken) throw unavailable();
    const grantBody = ['mpg1', 'stove', config.unityEnvironment, identity.userId,
      createHash('sha256').update(modulus).digest('hex'), seconds() + 600].join('|');
    return { channel: 'stove', environment: config.unityEnvironment, playerId: identity.userId,
      idToken: identity.idToken, sessionToken: identity.sessionToken, grantBody,
      grantSignature: sign('RSA-SHA256', Buffer.from(grantBody), signingKey).toString('base64') };
  };
}
