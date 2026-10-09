#!/usr/bin/env node
require('dotenv').config();
const { Bot } = require('grammy');

const token = process.env.BOT_TOKEN;
if (!token) {
  console.error('BOT_TOKEN env var missing');
  process.exit(1);
}

const bot = new Bot(token);

// Topic ID (set via config or env)
const IMAGE_TOPIC = Number(process.env.IMAGE_TOPIC || 0);
const WRITER_TOPIC = Number(process.env.WRITER_TOPIC || 0);

// Simple command: /tulis <prompt>
bot.command('tulis', async (ctx) => {
  // Only respond in Writer topic
  if (WRITER_TOPIC && ctx.message.message_thread_id !== WRITER_TOPIC) {
    return; // ignore outside topic
  }
  const prompt = ctx.message.text.replace(/^\/tulis\s+/i, '').trim();
  if (!prompt) {
    await ctx.reply('🚫 Silakan beri prompt setelah /tulis');
    return;
  }
  // Placeholder AI generation – replace with real LLM call
  const fakeResult = `📝 Hasil tulisan untuk "${prompt}":\n\nLorem ipsum dolor sit amet, consectetur adipiscing elit.`;
  await ctx.reply(fakeResult);
});

bot.command('start', async (ctx) => {
  await ctx.reply('🤖 AI‑Writer Bot siap! Gunakan /tulis <prompt> di topik yang tepat.');
});

bot.catch((err) => {
  console.error('Bot error:', err);
});

bot.start();
