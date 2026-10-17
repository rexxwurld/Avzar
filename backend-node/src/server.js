import { createServer } from 'node:http';
import { loadEnv } from './config/env.js';
import { createApp } from './http/app.js';

const env = loadEnv();
const server = createServer(createApp(env));
server.listen(env.PORT, env.HOST, () => {
  console.info(JSON.stringify({ level: 'info', event: 'server_started', host: env.HOST, port: env.PORT, environment: env.NODE_ENV }));
});

function shutdown(signal) {
  console.info(JSON.stringify({ level: 'info', event: 'shutdown_started', signal }));
  server.close((error) => {
    if (error) {
      console.error(JSON.stringify({ level: 'error', event: 'shutdown_failed', message: error.message }));
      process.exitCode = 1;
    }
  });
}
process.once('SIGINT', () => shutdown('SIGINT'));
process.once('SIGTERM', () => shutdown('SIGTERM'));
