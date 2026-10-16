import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { BridgeError, createBridge } from './bridge.mjs';

export function makeServer(authenticate, { now = Date.now } = {}) {
  const attempts = new Map();
  let inFlight = 0, nextSweep = 0;
  const server = createServer(async (request, response) => {
    const reply = (status, value) => {
      response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store',
        'X-Content-Type-Options': 'nosniff', ...(status === 429 ? { 'Retry-After': '60' } : {}) });
      response.end(JSON.stringify(value));
    };
    if (request.url === '/healthz' && request.method === 'GET') return reply(200, { status: 'ok' });
    if (request.url !== '/v1/stove/auth' || request.method !== 'POST') return reply(404, { error: 'not_found' });
    const time = now();
    if (time >= nextSweep) {
      for (const [ip, entry] of attempts) if (entry.until <= time) attempts.delete(ip);
      nextSweep = time + 10000;
    }
    // Do not trust caller-supplied X-Forwarded-For. A reverse proxy needs its own
    // per-client limit; this socket limit remains a second defensive boundary.
    const ip = request.socket.remoteAddress || 'unknown';
    let entry = attempts.get(ip);
    if (!entry || entry.until <= time) {
      if (attempts.size >= 10000) return reply(429, { error: 'try_later' });
      entry = { until: time + 60000, count: 0 }; attempts.set(ip, entry);
    }
    if (++entry.count > 30 || inFlight >= 16) return reply(429, { error: 'try_later' });
    if (request.headers['content-type']?.split(';')[0].trim() !== 'application/json' ||
        Number(request.headers['content-length']) > 12288) return reply(400, { error: 'invalid_request' });
    inFlight++;
    try {
      const chunks = []; let size = 0;
      for await (const chunk of request) {
        size += chunk.length;
        if (size > 12288) { reply(413, { error: 'request_too_large' }); return; }
        chunks.push(chunk);
      }
      let input;
      try { input = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
      catch { throw new BridgeError(400, 'invalid_request'); }
      reply(200, await authenticate(input));
    } catch (error) {
      if (!response.headersSent) reply(error instanceof BridgeError ? error.status : 503,
        { error: error instanceof BridgeError ? error.code : 'authentication_unavailable' });
      // Never log request bodies, authorization headers, account IDs or errors
      // from upstream APIs. Hosting may record aggregate status/latency only.
    } finally { inFlight--; }
  });
  server.requestTimeout = 15000;
  server.headersTimeout = 10000;
  server.timeout = 30000;
  return server;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const env = process.env;
  const authenticate = createBridge({
    stoveBaseUrl: env.STOVE_API_BASE_URL?.replace(/\/$/, ''), stoveGameId: env.STOVE_GAME_ID,
    stoveClientId: env.STOVE_CLIENT_ID, stoveClientSecret: env.STOVE_CLIENT_SECRET,
    stoveIdField: env.STOVE_ID_FIELD || 'guid', instanceId: env.INSTANCE_ID,
    unityProjectId: env.UGS_PROJECT_ID, unityEnvironmentId: env.UGS_ENVIRONMENT_ID,
    unityEnvironment: env.UGS_ENVIRONMENT_NAME, unityKeyId: env.UGS_KEY_ID,
    unityKeySecret: env.UGS_KEY_SECRET, signingKey: readFileSync(env.GRANT_PRIVATE_KEY_FILE, 'utf8')
  });
  const server = makeServer(authenticate);
  server.listen(Number(env.PORT || 8080), env.HOST || '127.0.0.1', () => console.log('Authentication bridge ready.'));
  process.on('SIGTERM', () => server.close());
}
