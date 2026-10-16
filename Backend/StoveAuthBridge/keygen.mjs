import { generateKeyPairSync } from 'node:crypto';
import { writeFileSync } from 'node:fs';
const { privateKey, publicKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
// wx prevents overwriting a deployed signing key and invalidating every client.
writeFileSync('grant-private.pem', privateKey.export({ type: 'pkcs8', format: 'pem' }), { flag: 'wx', mode: 0o600 });
const jwk = publicKey.export({ format: 'jwk' });
console.log('grantKeyModulus (PUBLIC; copy to the local Unity platform settings):');
console.log(Buffer.from(jwk.n, 'base64url').toString('base64'));
