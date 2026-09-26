import express from 'express';
import { createServer } from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Server } from 'socket.io';
import { GameManager } from './game.js';

import fs from 'node:fs';
import { DEFAULT_PROPS } from '../shared/props.js';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const customMapFile = path.join(root, 'shared', 'custom-map.json');

function getMapProps() {
  try {
    if (fs.existsSync(customMapFile)) {
      const data = JSON.parse(fs.readFileSync(customMapFile, 'utf8'));
      if (Array.isArray(data.props)) return data.props;
    }
  } catch (e) {
    console.error('Failed reading custom map, falling back to defaults:', e);
  }
  return DEFAULT_PROPS;
}

export async function createGameServer({ serveClient = true, production = process.env.NODE_ENV === 'production' || process.argv.includes('--production'), now } = {}) {
  const app = express();
  app.use(express.json({ limit: '2mb' }));
  const httpServer = createServer(app);
  const io = new Server(httpServer, { maxHttpBufferSize: 4096, pingTimeout: 20000, serveClient: false });
  // Authoritative recipients exclude a departing player before async Socket.IO
  // room cleanup, preventing a stale playing snapshot from reopening their game.
  const game = new GameManager({ now, onChange: (_code, state) => io.to(state.players.map((player) => player.id)).emit('room:state', state) });
  game.syncCustomMap(getMapProps());
  let vite;

  app.get('/api/health', (_request, response) => response.json({ ok: true, rooms: game.rooms.size }));

  app.get('/api/map', (_request, response) => {
    response.json({ ok: true, props: getMapProps(), isCustom: fs.existsSync(customMapFile) });
  });

  app.post('/api/map', (request, response) => {
    const props = request.body?.props;
    if (!Array.isArray(props)) {
      return response.status(400).json({ ok: false, error: 'Очікується масив об’єктів props.' });
    }
    try {
      fs.writeFileSync(customMapFile, JSON.stringify({ props, updated: new Date().toISOString() }, null, 2), 'utf8');
      game.syncCustomMap(props);
      response.json({ ok: true, count: props.length });
    } catch (e) {
      response.status(500).json({ ok: false, error: 'Не вдалося зберегти мапу: ' + e.message });
    }
  });

  app.post('/api/map/reset', (_request, response) => {
    try {
      if (fs.existsSync(customMapFile)) {
        fs.unlinkSync(customMapFile);
      }
      game.syncCustomMap(DEFAULT_PROPS);
      response.json({ ok: true, count: DEFAULT_PROPS.length });
    } catch (e) {
      response.status(500).json({ ok: false, error: 'Не вдалося скинути мапу: ' + e.message });
    }
  });
  if (serveClient) {
    if (production) {
      app.use(express.static(path.join(root, 'dist')));
      app.use((_request, response) => response.sendFile(path.join(root, 'dist', 'index.html')));
    } else {
      const { createServer: createViteServer } = await import('vite');
      vite = await createViteServer({ root, server: { middlewareMode: true, hmr: { server: httpServer } }, appType: 'spa' });
      app.use(vite.middlewares);
    }
  }

  function serverLog(category, action, data = null) {
    const time = new Date().toLocaleTimeString('uk-UA', { hour12: false });
    if (data) {
      console.log(`[DEAD AIR // SERVER // ${category}] [${time}] ${action}`, JSON.stringify(data));
    } else {
      console.log(`[DEAD AIR // SERVER // ${category}] [${time}] ${action}`);
    }
  }

  io.on('connection', (socket) => {
    serverLog('SOCKET', `Гравець підключився: ${socket.id}`);
    const reply = (ack, result) => { if (typeof ack === 'function') ack(result); };
    const object = (input) => input && typeof input === 'object' && !Array.isArray(input) ? input : {};
    const changeRoom = async (method, input, ack) => {
      const oldCode = game.playerRooms.get(socket.id);
      const parsed = object(input);
      const result = game[method](socket.id, parsed);
      if (result.ok) {
        if (oldCode && oldCode !== result.code) await socket.leave(oldCode);
        await socket.join(result.code);
        game.publish(game.roomFor(socket.id));
        serverLog('ROOM', `${method === 'createRoom' ? 'Створено кімнату' : 'Приєднано до кімнати'} [${result.code}] гравцем ${socket.id} (ім'я: "${parsed.name}", клас: ${parsed.characterClass || 'default'})`);
      } else {
        serverLog('ROOM', `Помилка ${method} для ${socket.id}: ${result.error}`);
      }
      reply(ack, result);
    };
    socket.on('room:create', (input, ack) => changeRoom('createRoom', input, ack));
    socket.on('room:join', (input, ack) => changeRoom('joinRoom', input, ack));
    socket.on('room:leave', async (ack) => {
      const code = game.playerRooms.get(socket.id);
      const result = game.leaveRoom(socket.id);
      if (code) await socket.leave(code);
      serverLog('ROOM', `Гравець ${socket.id} залишив кімнату [${code || 'none'}]`);
      reply(ack, result);
    });
    socket.on('game:start', (ack) => {
      serverLog('GAME', `Хост ${socket.id} натиснув ПОЧАТИ ЗАПИС`);
      reply(ack, game.startGame(socket.id));
    });
    socket.on('game:restart', (ack) => {
      serverLog('GAME', `Хост ${socket.id} перезапустив сеанс`);
      reply(ack, game.restartGame(socket.id));
    });
    socket.on('game:pause', (input, ack) => {
      const parsed = object(input);
      serverLog('GAME', `Пауза ${socket.id}: ${Boolean(parsed.paused)}`);
      reply(ack, game.pauseGame(socket.id, parsed));
    });
    socket.on('player:move', (input) => game.movePlayer(socket.id, object(input)));
    socket.on('player:class', (input, ack) => {
      const parsed = object(input);
      serverLog('CLASS', `Зміна класу ${socket.id}: ${parsed.characterClass}`);
      reply(ack, game.setPlayerClass(socket.id, parsed));
    });
    socket.on('player:radio', (input, ack) => {
      const parsed = object(input);
      serverLog('RADIO', `Рація ${socket.id}: фраза #${parsed.phrase ?? 0}`);
      reply(ack, game.radioPing(socket.id, parsed));
    });
    socket.on('game:interact', (input, ack) => {
      const parsed = object(input);
      serverLog('INTERACT', `Взаємодія ${socket.id}: об'єкт "${parsed.id}"`);
      reply(ack, game.interact(socket.id, parsed));
    });
    socket.on('disconnect', () => {
      serverLog('SOCKET', `Гравець відключився: ${socket.id}`);
      game.leaveRoom(socket.id);
    });
  });

  const timer = setInterval(() => game.tick(), 1000 / 12);
  timer.unref();
  return {
    app, httpServer, io, game,
    async listen(port = 3000, host = '0.0.0.0') {
      await new Promise((resolve, reject) => {
        httpServer.once('error', reject);
        httpServer.listen(port, host, () => { httpServer.off('error', reject); resolve(); });
      });
      return httpServer.address();
    },
    async close() {
      clearInterval(timer);
      if (vite) await vite.close();
      await new Promise((resolve) => io.close(resolve));
      if (httpServer.listening) await new Promise((resolve) => httpServer.close(resolve));
    },
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const server = await createGameServer();
  const port = Number(process.env.PORT) || 3000;
  await server.listen(port);
  console.log(`DEAD AIR is broadcasting at http://localhost:${port}`);
  const shutdown = async () => { await server.close(); process.exit(0); };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}
